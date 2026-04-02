using FluentValidation;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.DTOs.Channels;
using Misty.Application.DTOs.Common;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Application.Interfaces.Channels;
using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Application.Services;

public class ChannelMemberService : ChannelServiceBase, IChannelMemberService
{
    private readonly IChannelRepository _channelRepository;
    private readonly IBlobStorageProvider _blobStorage;
    private readonly IValidator<MarkChannelReadRequest> _markReadValidator;
    private readonly IValidator<UpdateChannelMemberRolesRequest> _updateRolesValidator;
    private readonly ILogger<ChannelMemberService> _logger;

    public ChannelMemberService(
        IChannelRepository channelRepository,
        IBlobStorageProvider blobStorage,
        IValidator<MarkChannelReadRequest> markReadValidator,
        IValidator<UpdateChannelMemberRolesRequest> updateRolesValidator,
        ILogger<ChannelMemberService> logger)
        : base(channelRepository)
    {
        _channelRepository = channelRepository;
        _blobStorage = blobStorage;
        _markReadValidator = markReadValidator;
        _updateRolesValidator = updateRolesValidator;
        _logger = logger;
    }

    // UC-6.1 List Channel Members
    public async Task<PagedResponse<ChannelMemberResponse>> GetMembersAsync(
        Guid channelId, string userId, int page, int pageSize, CancellationToken ct = default)
    {
        await GetRequiredActiveMemberAsync(channelId, userId, ct);

        var (items, totalCount) = await _channelRepository.GetMembersPagedAsync(channelId, page, pageSize, ct);

        var responses = new List<ChannelMemberResponse>(items.Count);
        foreach (var member in items)
            responses.Add(await ToMemberResponseAsync(member, ct));

        return new PagedResponse<ChannelMemberResponse>
        {
            Items = responses,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // UC-6.2 Get Channel Member
    public async Task<ChannelMemberResponse> GetMemberAsync(
        Guid channelId, Guid memberId, string userId, CancellationToken ct = default)
    {
        await GetRequiredActiveMemberAsync(channelId, userId, ct);

        var member = await _channelRepository.GetMemberByIdAsync(memberId, ct);
        if (member is null || member.ChannelId != channelId)
            throw new NotFoundException("ChannelMember", memberId);

        return await ToMemberResponseAsync(member, ct);
    }

    // UC-6.3 Leave Channel
    public async Task LeaveChannelAsync(
        Guid channelId, string userId, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);

        if (member.Channel.OwnerUserId == userId)
            throw new BusinessRuleException("The channel owner cannot leave. Transfer ownership first.");

        if (member.LeftAt is not null)
            return; // Already left (concurrent request)

        member.LeftAt = DateTimeOffset.UtcNow;
        // MemberCount is decremented automatically by ApplicationDbContext.OnBeforeSave

        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation("User {UserId} left channel {ChannelId}", userId, channelId);
    }

    // UC-6.4 Remove Member (Kick)
    public async Task RemoveMemberAsync(
        Guid channelId, Guid memberId, string userId, CancellationToken ct = default)
    {
        var actorMember = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(actorMember, ChannelPermission.KickUsers);

        var targetMember = await _channelRepository.GetMemberByIdAsync(memberId, ct);
        if (targetMember is null || targetMember.ChannelId != channelId)
            throw new NotFoundException("ChannelMember", memberId);

        if (targetMember.UserId == userId)
            throw new BusinessRuleException("You cannot kick yourself. Use leave instead.");

        PermissionHelper.EnsureOutranks(actorMember, targetMember);

        if (targetMember.LeftAt is not null)
            return;

        targetMember.LeftAt = DateTimeOffset.UtcNow;
        // MemberCount is decremented automatically by ApplicationDbContext.OnBeforeSave

        await AddAuditLogAsync(channelId, userId, AuditAction.MemberKicked, ct,
            "ChannelMember", memberId.ToString());
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Member {MemberId} kicked from channel {ChannelId} by {UserId}",
            memberId, channelId, userId);
    }

    // UC-6.5 Mark Channel as Read
    public async Task MarkChannelReadAsync(
        Guid channelId, string userId, MarkChannelReadRequest request, CancellationToken ct = default)
    {
        await _markReadValidator.ValidateAndThrowAsync(request, ct);

        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);

        var now = DateTimeOffset.UtcNow;
        member.LastReadAt = request.LastReadAt > now ? now : request.LastReadAt;

        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Channel {ChannelId} marked as read by {UserId}", channelId, userId);
    }

    // UC-6.6 Update Member Roles
    public async Task<ChannelMemberResponse> UpdateMemberRolesAsync(
        Guid channelId, Guid memberId, string userId,
        UpdateChannelMemberRolesRequest request, CancellationToken ct = default)
    {
        await _updateRolesValidator.ValidateAndThrowAsync(request, ct);

        var actorMember = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(actorMember, ChannelPermission.ManageRoles);

        var targetMember = await _channelRepository.GetMemberByIdAsync(memberId, ct);
        if (targetMember is null || targetMember.ChannelId != channelId)
            throw new NotFoundException("ChannelMember", memberId);

        PermissionHelper.EnsureOutranks(actorMember, targetMember);

        // Validate all provided role IDs exist and belong to this channel
        var desiredRoleIds = request.RoleIds.Distinct().ToList();
        if (desiredRoleIds.Count > 0)
        {
            var foundRoles = await _channelRepository.GetChannelRolesByIdsAsync(channelId, desiredRoleIds, ct);
            if (foundRoles.Count != desiredRoleIds.Count)
            {
                var foundIds = foundRoles.Select(r => r.ChannelRoleId).ToHashSet();
                var missing = desiredRoleIds.First(id => !foundIds.Contains(id));
                throw new NotFoundException("ChannelRole", missing);
            }

            // Prevent manual assignment of system roles
            if (foundRoles.Any(r => r.IsSystemRole))
                throw new BusinessRuleException("System roles cannot be assigned manually.");
        }

        // Compute diff between current and desired roles
        var currentRoles = await _channelRepository.GetMemberRolesAsync(targetMember.ChannelMemberId, ct);
        var currentRoleIds = currentRoles.Select(r => r.ChannelRoleId).ToHashSet();
        var desiredRoleIdSet = desiredRoleIds.ToHashSet();

        var toRemove = currentRoles.Where(r => !desiredRoleIdSet.Contains(r.ChannelRoleId)).ToList();
        var toAdd = desiredRoleIds.Where(id => !currentRoleIds.Contains(id)).ToList();

        foreach (var assignment in toRemove)
        {
            _channelRepository.RemoveMemberRoleAsync(assignment);
            await AddAuditLogAsync(channelId, userId, AuditAction.MemberRoleRemoved, ct,
                "ChannelMember", memberId.ToString());
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var roleId in toAdd)
        {
            await _channelRepository.AddMemberRoleAsync(new ChannelMemberRole
            {
                ChannelMemberId = targetMember.ChannelMemberId,
                ChannelRoleId = roleId,
                AssignedAt = now
            }, ct);
            await AddAuditLogAsync(channelId, userId, AuditAction.MemberRoleAssigned, ct,
                "ChannelMember", memberId.ToString());
        }

        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Roles updated for member {MemberId} in channel {ChannelId} by {UserId} (added: {Added}, removed: {Removed})",
            memberId, channelId, userId, toAdd.Count, toRemove.Count);

        // Re-fetch to get updated roles
        var updated = await _channelRepository.GetMemberByIdAsync(memberId, ct);
        return await ToMemberResponseAsync(updated!, ct);
    }

    // Helpers

    private async Task<ChannelMemberResponse> ToMemberResponseAsync(
        ChannelMember member, CancellationToken ct)
    {
        string? avatarUrl = null;
        if (member.User.Avatar is not null)
            avatarUrl = await _blobStorage.GetDownloadUrlAsync(member.User.Avatar.StoragePath, ct);

        return new ChannelMemberResponse
        {
            ChannelMemberId = member.ChannelMemberId,
            User = new UserSummary
            {
                Id = member.User.UserId,
                DisplayName = member.User.DisplayName,
                AvatarUrl = avatarUrl
            },
            JoinedAt = member.JoinedAt,
            Roles = member.AssignedRoles.Select(ar => new ChannelRoleSummary
            {
                ChannelRoleId = ar.Role.ChannelRoleId,
                Name = ar.Role.Name,
                Position = ar.Role.Position
            }).ToList()
        };
    }
}
