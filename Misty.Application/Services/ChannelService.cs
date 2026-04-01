using System.Security.Cryptography;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.DTOs.Channels;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Application.Interfaces.Channels;
using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Application.Services;

public class ChannelService : IChannelService
{
    private readonly IChannelRepository _channelRepository;
    private readonly IBlobStorageProvider _blobStorage;
    private readonly IValidator<CreateChannelRequest> _createValidator;
    private readonly IValidator<UpdateChannelRequest> _updateValidator;
    private readonly IValidator<TransferOwnershipRequest> _transferValidator;
    private readonly ILogger<ChannelService> _logger;

    public ChannelService(
        IChannelRepository channelRepository,
        IBlobStorageProvider blobStorage,
        IValidator<CreateChannelRequest> createValidator,
        IValidator<UpdateChannelRequest> updateValidator,
        IValidator<TransferOwnershipRequest> transferValidator,
        ILogger<ChannelService> logger)
    {
        _channelRepository = channelRepository;
        _blobStorage = blobStorage;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _transferValidator = transferValidator;
        _logger = logger;
    }

    // UC-5.1 Create Channel
    public async Task<ChannelDetailResponse> CreateChannelAsync(
        string userId, CreateChannelRequest request, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);

        var now = DateTimeOffset.UtcNow;
        var channelId = Guid.NewGuid();

        var channel = new Channel
        {
            ChannelId = channelId,
            Name = request.Name,
            Description = request.Description,
            IsPrivate = request.IsPrivate,
            OwnerUserId = userId,
            CreatedAt = now,
            DefaultPermissions = ChannelPermission.SendMessages | ChannelPermission.AddReactions | ChannelPermission.AttachFiles,
            MemberCount = 1
        };

        await _channelRepository.AddChannelAsync(channel, ct);

        // System roles
        var ownerRole = new ChannelRole
        {
            ChannelRoleId = Guid.NewGuid(),
            ChannelId = channelId,
            Name = "Owner",
            IsSystemRole = true,
            Permissions = ChannelPermission.Administrator,
            Position = 100,
            CreatedAt = now
        };

        var moderatorRole = new ChannelRole
        {
            ChannelRoleId = Guid.NewGuid(),
            ChannelId = channelId,
            Name = "Moderator",
            IsSystemRole = true,
            Permissions = ChannelPermission.DeleteMessages | ChannelPermission.MuteUsers | ChannelPermission.BanUsers | ChannelPermission.ViewAuditLog,
            Position = 50,
            CreatedAt = now
        };

        await _channelRepository.AddRoleAsync(ownerRole, ct);
        await _channelRepository.AddRoleAsync(moderatorRole, ct);

        // Creator membership
        var member = new ChannelMember
        {
            ChannelMemberId = Guid.NewGuid(),
            UserId = userId,
            ChannelId = channelId,
            JoinedAt = now
        };

        await _channelRepository.AddMemberAsync(member, ct);

        // Assign Owner role to creator
        var memberRole = new ChannelMemberRole
        {
            ChannelMemberId = member.ChannelMemberId,
            ChannelRoleId = ownerRole.ChannelRoleId,
            AssignedAt = now
        };

        await _channelRepository.AddMemberRoleAsync(memberRole, ct);

        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Channel {ChannelId} created by {UserId}", channelId, userId);

        // Re-fetch to get full navigation properties populated
        var saved = await _channelRepository.GetByIdAsync(channelId, ct);
        var savedMember = await _channelRepository.GetActiveMemberAsync(channelId, userId, ct);
        return await ToDetailResponseAsync(saved!, savedMember!, ct);
    }

    // UC-5.2 Get Channel Detail
    public async Task<ChannelDetailResponse> GetChannelAsync(
        Guid channelId, string userId, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        return await ToDetailResponseAsync(member.Channel, member, ct);
    }

    // UC-5.3 List User Channels
    public async Task<IReadOnlyList<ChannelSummary>> GetUserChannelsAsync(
        string userId, CancellationToken ct = default)
    {
        var members = await _channelRepository.GetUserChannelsAsync(userId, ct);

        var summaries = new List<ChannelSummary>(members.Count);
        foreach (var member in members)
        {
            var channel = member.Channel;
            var unreadCount = await _channelRepository.GetUnreadCountAsync(
                channel.ChannelId, userId, member.LastReadAt, ct);

            string? iconUrl = null;
            if (channel.Icon is not null)
                iconUrl = await _blobStorage.GetDownloadUrlAsync(channel.Icon.StoragePath, ct);

            summaries.Add(new ChannelSummary
            {
                ChannelId = channel.ChannelId,
                Name = channel.Name,
                IconUrl = iconUrl,
                IsPrivate = channel.IsPrivate,
                MemberCount = channel.MemberCount,
                LastMessageAt = channel.LastMessageAt,
                UnreadCount = unreadCount
            });
        }

        return summaries;
    }

    // UC-5.4 Update Channel
    public async Task<ChannelDetailResponse> UpdateChannelAsync(
        Guid channelId, string userId, UpdateChannelRequest request, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);

        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.EditChannel);

        var channel = member.Channel;

        if (request.Name is not null)
            channel.Name = request.Name;

        if (request.Description is not null)
            channel.Description = request.Description;

        if (request.IsPrivate.HasValue)
            channel.IsPrivate = request.IsPrivate.Value;

        if (request.IsAiAssistantEnabled.HasValue)
            channel.IsAiAssistantEnabled = request.IsAiAssistantEnabled.Value;

        if (request.DefaultPermissions.HasValue)
            channel.DefaultPermissions = request.DefaultPermissions.Value;

        channel.Version = request.Version;

        await AddAuditLogAsync(channelId, userId, AuditAction.ChannelUpdated, ct);
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Channel {ChannelId} updated by {UserId}", channelId, userId);

        // Re-fetch for consistent response
        member = await _channelRepository.GetActiveMemberAsync(channelId, userId, ct);
        return await ToDetailResponseAsync(member!.Channel, member, ct);
    }

    // UC-5.5 Delete Channel
    public async Task DeleteChannelAsync(
        Guid channelId, string userId, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);

        if (member.Channel.OwnerUserId != userId)
            throw new BusinessRuleException("Only the channel owner can delete the channel.");

        member.Channel.DeletedAt = DateTimeOffset.UtcNow;

        await AddAuditLogAsync(channelId, userId, AuditAction.ChannelDeleted, ct);
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Channel {ChannelId} deleted by {UserId}", channelId, userId);
    }

    // UC-5.6 Join Channel by Invite Code
    public async Task<ChannelDetailResponse> JoinByInviteCodeAsync(
        string inviteCode, string userId, CancellationToken ct = default)
    {
        var channel = await _channelRepository.GetByInviteCodeAsync(inviteCode, ct)
            ?? throw new NotFoundException("Channel", inviteCode);

        var existingMember = await _channelRepository.GetActiveMemberAsync(channel.ChannelId, userId, ct);
        if (existingMember is not null)
            throw new BusinessRuleException("You are already a member of this channel.");

        if (await _channelRepository.HasActiveBanAsync(channel.ChannelId, userId, ct))
            throw new BusinessRuleException("You are banned from this channel.");

        var member = new ChannelMember
        {
            ChannelMemberId = Guid.NewGuid(),
            UserId = userId,
            ChannelId = channel.ChannelId,
            JoinedAt = DateTimeOffset.UtcNow
        };

        await _channelRepository.AddMemberAsync(member, ct);
        // MemberCount is incremented automatically by ApplicationDbContext.OnBeforeSave

        try
        {
            await _channelRepository.SaveChangesAsync(ct);
        }
        catch (DuplicateException)
        {
            throw new BusinessRuleException("You are already a member of this channel.");
        }

        _logger.LogInformation("User {UserId} joined channel {ChannelId} via invite code", userId, channel.ChannelId);

        var savedMember = await _channelRepository.GetActiveMemberAsync(channel.ChannelId, userId, ct);
        return await ToDetailResponseAsync(savedMember!.Channel, savedMember, ct);
    }

    // UC-5.7 Generate Invite Code
    public async Task<string> GenerateInviteCodeAsync(
        Guid channelId, string userId, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.ManageInvites);

        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var inviteCode = GenerateInviteCode();
            member.Channel.InviteCode = inviteCode;

            try
            {
                await AddAuditLogAsync(channelId, userId, AuditAction.InviteCreated, ct);
                await _channelRepository.SaveChangesAsync(ct);

                _logger.LogInformation("Invite code generated for channel {ChannelId} by {UserId}", channelId, userId);
                return inviteCode;
            }
            catch (DuplicateException) when (attempt < maxAttempts)
            {
                _logger.LogWarning("Invite code collision on attempt {Attempt} for channel {ChannelId}, retrying", attempt, channelId);
            }
        }

        throw new BusinessRuleException("Failed to generate a unique invite code. Please try again.");
    }

    // UC-5.8 Revoke Invite Code
    public async Task RevokeInviteCodeAsync(
        Guid channelId, string userId, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.ManageInvites);

        member.Channel.InviteCode = null;

        await AddAuditLogAsync(channelId, userId, AuditAction.InviteRevoked, ct);
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Invite code revoked for channel {ChannelId} by {UserId}", channelId, userId);
    }

    // UC-5.9 Transfer Ownership
    public async Task TransferOwnershipAsync(
        Guid channelId, string userId, TransferOwnershipRequest request, CancellationToken ct = default)
    {
        await _transferValidator.ValidateAndThrowAsync(request, ct);

        var channel = await _channelRepository.GetByIdAsync(channelId, ct)
            ?? throw new NotFoundException("Channel", channelId);

        if (channel.OwnerUserId != userId)
            throw new BusinessRuleException("Only the channel owner can transfer ownership.");

        if (request.NewOwnerUserId == userId)
            throw new BusinessRuleException("Cannot transfer ownership to yourself.");

        var oldOwnerMember = await _channelRepository.GetActiveMemberAsync(channelId, userId, ct)!;
        var newOwnerMember = await _channelRepository.GetActiveMemberAsync(channelId, request.NewOwnerUserId, ct)
            ?? throw new NotFoundException("ChannelMember", request.NewOwnerUserId);

        channel.OwnerUserId = request.NewOwnerUserId;

        // Swap Owner system role assignment
        var ownerRole = await _channelRepository.GetSystemRoleAsync(channelId, "Owner", ct);
        if (ownerRole is not null)
        {
            // Remove Owner role from old owner (if assigned)
            var oldAssignment = await _channelRepository.GetMemberRoleAssignmentAsync(
                oldOwnerMember!.ChannelMemberId, ownerRole.ChannelRoleId, ct);
            if (oldAssignment is not null)
                _channelRepository.RemoveMemberRoleAsync(oldAssignment);

            // Assign Owner role to new owner (if not already assigned)
            var existingAssignment = await _channelRepository.GetMemberRoleAssignmentAsync(
                newOwnerMember.ChannelMemberId, ownerRole.ChannelRoleId, ct);
            if (existingAssignment is null)
            {
                await _channelRepository.AddMemberRoleAsync(new ChannelMemberRole
                {
                    ChannelMemberId = newOwnerMember.ChannelMemberId,
                    ChannelRoleId = ownerRole.ChannelRoleId,
                    AssignedAt = DateTimeOffset.UtcNow
                }, ct);
            }
        }

        await AddAuditLogAsync(channelId, userId, AuditAction.OwnershipTransferred, ct,
            "User", request.NewOwnerUserId);
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Channel {ChannelId} ownership transferred from {OldOwner} to {NewOwner}",
            channelId, userId, request.NewOwnerUserId);
    }

    // UC-5.10 Set Channel Icon
    public async Task SetChannelIconAsync(
        Guid channelId, string userId, Guid attachmentId, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.EditChannel);

        var attachment = await _channelRepository.GetAttachmentByIdAsync(attachmentId, ct)
            ?? throw new NotFoundException("Attachment", attachmentId);

        if (attachment.Purpose != AttachmentPurpose.ChannelIcon)
            throw new BusinessRuleException("Attachment is not a channel icon.");

        if (attachment.UploadedByUserId != userId)
            throw new BusinessRuleException("You can only use attachments you uploaded.");

        member.Channel.IconAttachmentId = attachmentId;

        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Channel icon set for {ChannelId} by {UserId}", channelId, userId);
    }

    // UC-5.11 Remove Channel Icon
    public async Task RemoveChannelIconAsync(
        Guid channelId, string userId, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.EditChannel);

        member.Channel.IconAttachmentId = null;

        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Channel icon removed for {ChannelId} by {UserId}", channelId, userId);
    }

    // Helpers

    private async Task<ChannelMember> GetRequiredActiveMemberAsync(
        Guid channelId, string userId, CancellationToken ct)
    {
        return await _channelRepository.GetActiveMemberAsync(channelId, userId, ct)
            ?? throw new NotFoundException("Channel", channelId);
    }

    private async Task AddAuditLogAsync(
        Guid channelId, string userId, AuditAction action, CancellationToken ct,
        string? targetType = null, string? targetId = null)
    {
        var auditLog = new ChannelAuditLog
        {
            ChannelAuditLogId = Guid.NewGuid(),
            ChannelId = channelId,
            ActorUserId = userId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _channelRepository.AddAuditLogAsync(auditLog, ct);
    }

    private async Task<ChannelDetailResponse> ToDetailResponseAsync(
        Channel channel, ChannelMember member, CancellationToken ct)
    {
        var effectivePermissions = PermissionHelper.GetEffectivePermissions(member);

        string? iconUrl = null;
        if (channel.Icon is not null)
            iconUrl = await _blobStorage.GetDownloadUrlAsync(channel.Icon.StoragePath, ct);

        string? ownerAvatarUrl = null;
        if (channel.Owner.Avatar is not null)
            ownerAvatarUrl = await _blobStorage.GetDownloadUrlAsync(channel.Owner.Avatar.StoragePath, ct);

        // InviteCode visible only if user has ManageInvites or Administrator
        string? inviteCode = null;
        if (effectivePermissions.HasFlag(ChannelPermission.ManageInvites))
            inviteCode = channel.InviteCode;

        return new ChannelDetailResponse
        {
            ChannelId = channel.ChannelId,
            Name = channel.Name,
            Description = channel.Description,
            IconUrl = iconUrl,
            IsPrivate = channel.IsPrivate,
            IsAiAssistantEnabled = channel.IsAiAssistantEnabled,
            MemberCount = channel.MemberCount,
            CreatedAt = channel.CreatedAt,
            Owner = new UserSummary
            {
                Id = channel.Owner.UserId,
                DisplayName = channel.Owner.DisplayName,
                AvatarUrl = ownerAvatarUrl
            },
            InviteCode = inviteCode,
            MyPermissions = effectivePermissions,
            DefaultPermissions = channel.DefaultPermissions
        };
    }

    private static string GenerateInviteCode()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(18))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
