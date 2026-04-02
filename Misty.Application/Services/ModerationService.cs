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

public class ModerationService : ChannelServiceBase, IModerationService
{
    private readonly IChannelRepository _channelRepository;
    private readonly IBlobStorageProvider _blobStorage;
    private readonly IValidator<CreateModerationActionRequest> _createValidator;
    private readonly IValidator<RevokeModerationActionRequest> _revokeValidator;
    private readonly ILogger<ModerationService> _logger;

    public ModerationService(
        IChannelRepository channelRepository,
        IBlobStorageProvider blobStorage,
        IValidator<CreateModerationActionRequest> createValidator,
        IValidator<RevokeModerationActionRequest> revokeValidator,
        ILogger<ModerationService> logger)
        : base(channelRepository)
    {
        _channelRepository = channelRepository;
        _blobStorage = blobStorage;
        _createValidator = createValidator;
        _revokeValidator = revokeValidator;
        _logger = logger;
    }

    // UC-8.1 Create Moderation Action
    public async Task<ModerationActionResponse> CreateModerationActionAsync(
        Guid channelId, string userId, CreateModerationActionRequest request, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);

        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        EnsurePermissionForType(member, request.Type);

        if (request.TargetUserId == userId)
            throw new BusinessRuleException("You cannot moderate yourself.");

        var targetMember = await _channelRepository.GetActiveMemberAsync(channelId, request.TargetUserId, ct)
            ?? throw new NotFoundException("ChannelMember", request.TargetUserId);

        if (targetMember.Channel.OwnerUserId == request.TargetUserId)
            throw new BusinessRuleException("The channel owner cannot be moderated.");

        PermissionHelper.EnsureOutranks(member, targetMember);

        var existing = await _channelRepository.GetActiveModerationActionAsync(
            channelId, request.TargetUserId, request.Type, ct);
        if (existing is not null)
            throw new DuplicateException("ModerationAction", "ChannelId+TargetUserId+Type", request.Type.ToString());

        var action = new ModerationAction
        {
            ModerationActionId = Guid.NewGuid(),
            ChannelId = channelId,
            TargetUserId = request.TargetUserId,
            CreatedByUserId = userId,
            Type = request.Type,
            Reason = request.Reason,
            StartAt = DateTimeOffset.UtcNow,
            ExpiresAt = request.ExpiresAt,
            IsActive = true
        };

        await _channelRepository.AddModerationActionAsync(action, ct);

        // For bans, remove the target from the channel
        if (request.Type == ModerationType.Ban)
            targetMember.LeftAt = DateTimeOffset.UtcNow;

        await AddAuditLogAsync(channelId, userId, GetCreateAuditAction(request.Type), ct,
            "User", request.TargetUserId);
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Moderation action {ActionId} ({Type}) created in channel {ChannelId} targeting {TargetUserId} by {UserId}",
            action.ModerationActionId, request.Type, channelId, request.TargetUserId, userId);

        // Reload with includes for response mapping
        var saved = await _channelRepository.GetModerationActionByIdAsync(action.ModerationActionId, ct);
        return await ToResponseAsync(saved!, ct);
    }

    // UC-8.2 Revoke Moderation Action
    public async Task<ModerationActionResponse> RevokeModerationActionAsync(
        Guid channelId, Guid moderationActionId, string userId,
        RevokeModerationActionRequest request, CancellationToken ct = default)
    {
        await _revokeValidator.ValidateAndThrowAsync(request, ct);

        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);

        var action = await _channelRepository.GetModerationActionByIdAsync(moderationActionId, ct);
        if (action is null || action.ChannelId != channelId)
            throw new NotFoundException("ModerationAction", moderationActionId);

        EnsurePermissionForType(member, action.Type);

        if (!action.IsActive || (action.ExpiresAt.HasValue && action.ExpiresAt.Value <= DateTimeOffset.UtcNow))
            throw new BusinessRuleException("This moderation action is already revoked or expired.");

        action.IsActive = false;
        action.UpdatedByUserId = userId;
        // UpdatedAt is set automatically by OnBeforeSave

        await AddAuditLogAsync(channelId, userId, GetRevokeAuditAction(action.Type), ct,
            "ModerationAction", moderationActionId.ToString());
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Moderation action {ActionId} ({Type}) revoked in channel {ChannelId} by {UserId}",
            moderationActionId, action.Type, channelId, userId);

        return await ToResponseAsync(action, ct);
    }

    // UC-8.3 Get Moderation Action
    public async Task<ModerationActionResponse> GetModerationActionAsync(
        Guid channelId, Guid moderationActionId, string userId, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.ViewAuditLog);

        var action = await _channelRepository.GetModerationActionByIdAsync(moderationActionId, ct);
        if (action is null || action.ChannelId != channelId)
            throw new NotFoundException("ModerationAction", moderationActionId);

        return await ToResponseAsync(action, ct);
    }

    // UC-8.4 List Moderation Actions
    public async Task<PagedResponse<ModerationActionSummary>> GetModerationActionsAsync(
        Guid channelId, string userId, int page, int pageSize, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.ViewAuditLog);

        var (items, totalCount) = await _channelRepository.GetModerationActionsPagedAsync(
            channelId, page, pageSize, ct);

        return new PagedResponse<ModerationActionSummary>
        {
            Items = items.Select(ToSummary).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // UC-8.5 View Audit Log
    public async Task<PagedResponse<ChannelAuditLogResponse>> GetAuditLogAsync(
        Guid channelId, string userId, int page, int pageSize, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.ViewAuditLog);

        var (items, totalCount) = await _channelRepository.GetAuditLogsPagedAsync(
            channelId, page, pageSize, ct);

        return new PagedResponse<ChannelAuditLogResponse>
        {
            Items = items.Select(ToAuditLogResponse).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    // Helpers

    private static void EnsurePermissionForType(ChannelMember member, ModerationType type)
    {
        var required = type switch
        {
            ModerationType.Mute => ChannelPermission.MuteUsers,
            ModerationType.Ban => ChannelPermission.BanUsers,
            ModerationType.Warning => ChannelPermission.MuteUsers,
            _ => throw new BusinessRuleException($"Unknown moderation type: {type}.")
        };

        PermissionHelper.EnsurePermission(member, required);
    }

    private static AuditAction GetCreateAuditAction(ModerationType type) => type switch
    {
        ModerationType.Mute => AuditAction.MemberMuted,
        ModerationType.Ban => AuditAction.MemberBanned,
        ModerationType.Warning => AuditAction.MemberWarned,
        _ => AuditAction.MemberMuted
    };

    private static AuditAction GetRevokeAuditAction(ModerationType type) => type switch
    {
        ModerationType.Mute => AuditAction.MemberUnmuted,
        ModerationType.Ban => AuditAction.MemberUnbanned,
        ModerationType.Warning => AuditAction.WarningRevoked,
        _ => AuditAction.MemberUnmuted
    };

    private async Task<UserSummary> ToUserSummaryAsync(User user, CancellationToken ct)
    {
        string? avatarUrl = null;
        if (user.Avatar is not null)
            avatarUrl = await _blobStorage.GetDownloadUrlAsync(user.Avatar.StoragePath, ct);

        return new UserSummary
        {
            Id = user.UserId,
            DisplayName = user.DisplayName,
            AvatarUrl = avatarUrl
        };
    }

    private async Task<ModerationActionResponse> ToResponseAsync(ModerationAction action, CancellationToken ct)
    {
        return new ModerationActionResponse
        {
            ModerationActionId = action.ModerationActionId,
            Type = action.Type,
            TargetUser = await ToUserSummaryAsync(action.TargetUser, ct),
            Reason = action.Reason,
            StartAt = action.StartAt,
            ExpiresAt = action.ExpiresAt,
            IsActive = IsEffectivelyActive(action),
            CreatedBy = await ToUserSummaryAsync(action.CreatedBy, ct),
            UpdatedAt = action.UpdatedAt,
            UpdatedBy = action.UpdatedBy is not null ? await ToUserSummaryAsync(action.UpdatedBy, ct) : null
        };
    }

    private static bool IsEffectivelyActive(ModerationAction action)
    {
        return action.IsActive && (!action.ExpiresAt.HasValue || action.ExpiresAt.Value > DateTimeOffset.UtcNow);
    }

    private static ModerationActionSummary ToSummary(ModerationAction action)
    {
        return new ModerationActionSummary
        {
            ModerationActionId = action.ModerationActionId,
            Type = action.Type,
            TargetUserDisplayName = action.TargetUser.DisplayName,
            IsActive = IsEffectivelyActive(action),
            StartAt = action.StartAt,
            ExpiresAt = action.ExpiresAt
        };
    }

    private static ChannelAuditLogResponse ToAuditLogResponse(ChannelAuditLog log)
    {
        return new ChannelAuditLogResponse
        {
            ChannelAuditLogId = log.ChannelAuditLogId,
            Action = log.Action,
            ActorDisplayName = log.ActorDisplayName ?? log.Actor?.DisplayName,
            TargetType = log.TargetType,
            TargetId = log.TargetId,
            Details = log.Details,
            CreatedAt = log.CreatedAt
        };
    }
}
