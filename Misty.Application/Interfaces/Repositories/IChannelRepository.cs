using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Application.Interfaces;

public interface IChannelRepository
{
    Task<Channel?> GetByIdAsync(Guid channelId, CancellationToken ct = default);
    Task<Channel?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct = default);
    Task<IReadOnlyList<Channel>> GetUserChannelsAsync(string userId, CancellationToken ct = default);
    Task<ChannelMember?> GetActiveMemberAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task<ChannelMember?> GetMemberByIdAsync(Guid memberId, CancellationToken ct = default);
    Task<(IReadOnlyList<ChannelMember> Items, int TotalCount)> GetMembersPagedAsync(Guid channelId, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelRole>> GetChannelRolesByIdsAsync(Guid channelId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelMemberRole>> GetMemberRolesAsync(Guid channelMemberId, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelRole>> GetChannelRolesAsync(Guid channelId, CancellationToken ct = default);
    Task<ChannelRole?> GetRoleByIdAsync(Guid roleId, CancellationToken ct = default);
    Task<int> GetAssignedMemberCountAsync(Guid channelRoleId, CancellationToken ct = default);
    Task<Dictionary<Guid, int>> GetAssignedMemberCountsAsync(IEnumerable<Guid> roleIds, CancellationToken ct = default);
    Task<Attachment?> GetAttachmentByIdAsync(Guid attachmentId, CancellationToken ct = default);
    Task<bool> HasActiveBanAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid channelId, string userId, DateTimeOffset? lastReadAt, CancellationToken ct = default);
    Task AddChannelAsync(Channel channel, CancellationToken ct = default);
    Task AddMemberAsync(ChannelMember member, CancellationToken ct = default);
    Task AddRoleAsync(ChannelRole role, CancellationToken ct = default);
    void RemoveRole(ChannelRole role);
    Task AddMemberRoleAsync(ChannelMemberRole memberRole, CancellationToken ct = default);
    void RemoveMemberRoleAsync(ChannelMemberRole memberRole);
    Task<ChannelRole?> GetSystemRoleAsync(Guid channelId, string roleName, CancellationToken ct = default);
    Task<ChannelMemberRole?> GetMemberRoleAssignmentAsync(Guid channelMemberId, Guid channelRoleId, CancellationToken ct = default);
    Task AddAuditLogAsync(ChannelAuditLog auditLog, CancellationToken ct = default);

    // Moderation
    Task<ModerationAction?> GetModerationActionByIdAsync(Guid moderationActionId, CancellationToken ct = default);
    Task<ModerationAction?> GetActiveModerationActionAsync(Guid channelId, string targetUserId, ModerationType type, CancellationToken ct = default);
    Task<(IReadOnlyList<ModerationAction> Items, int TotalCount)> GetModerationActionsPagedAsync(Guid channelId, int page, int pageSize, CancellationToken ct = default);
    Task<(IReadOnlyList<ChannelAuditLog> Items, int TotalCount)> GetAuditLogsPagedAsync(Guid channelId, int page, int pageSize, CancellationToken ct = default);
    Task AddModerationActionAsync(ModerationAction action, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
