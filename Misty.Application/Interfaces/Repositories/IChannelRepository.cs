using Misty.Domain.Entities;

namespace Misty.Application.Interfaces;

public interface IChannelRepository
{
    Task<Channel?> GetByIdAsync(Guid channelId, CancellationToken ct = default);
    Task<Channel?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct = default);
    Task<IReadOnlyList<Channel>> GetUserChannelsAsync(string userId, CancellationToken ct = default);
    Task<ChannelMember?> GetActiveMemberAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task<Attachment?> GetAttachmentByIdAsync(Guid attachmentId, CancellationToken ct = default);
    Task<bool> HasActiveBanAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid channelId, string userId, DateTimeOffset? lastReadAt, CancellationToken ct = default);
    Task AddChannelAsync(Channel channel, CancellationToken ct = default);
    Task AddMemberAsync(ChannelMember member, CancellationToken ct = default);
    Task AddRoleAsync(ChannelRole role, CancellationToken ct = default);
    Task AddMemberRoleAsync(ChannelMemberRole memberRole, CancellationToken ct = default);
    void RemoveMemberRoleAsync(ChannelMemberRole memberRole);
    Task<ChannelRole?> GetSystemRoleAsync(Guid channelId, string roleName, CancellationToken ct = default);
    Task<ChannelMemberRole?> GetMemberRoleAssignmentAsync(Guid channelMemberId, Guid channelRoleId, CancellationToken ct = default);
    Task AddAuditLogAsync(ChannelAuditLog auditLog, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
