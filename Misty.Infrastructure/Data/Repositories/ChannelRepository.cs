using Microsoft.EntityFrameworkCore;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Infrastructure.Data.Repositories;

public class ChannelRepository : IChannelRepository
{
    private readonly ApplicationDbContext _db;

    public ChannelRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Channel?> GetByIdAsync(Guid channelId, CancellationToken ct = default)
    {
        return await _db.Channels
            .Include(c => c.Owner)
                .ThenInclude(u => u.Avatar)
            .Include(c => c.Icon)
            .FirstOrDefaultAsync(c => c.ChannelId == channelId, ct);
    }

    public async Task<Channel?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct = default)
    {
        return await _db.Channels
            .Include(c => c.Owner)
                .ThenInclude(u => u.Avatar)
            .Include(c => c.Icon)
            .FirstOrDefaultAsync(c => c.InviteCode == inviteCode, ct);
    }

    public async Task<IReadOnlyList<Channel>> GetUserChannelsAsync(string userId, CancellationToken ct = default)
    {
        return await _db.ChannelMembers
            .Where(cm => cm.UserId == userId && cm.LeftAt == null)
            .Include(cm => cm.Channel)
                .ThenInclude(c => c.Icon)
            .Select(cm => cm.Channel)
            .OrderByDescending(c => c.LastMessageAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<ChannelMember?> GetActiveMemberAsync(Guid channelId, string userId, CancellationToken ct = default)
    {
        return await _db.ChannelMembers
            .Include(cm => cm.AssignedRoles)
                .ThenInclude(ar => ar.Role)
            .Include(cm => cm.Channel)
                .ThenInclude(c => c.Owner)
                    .ThenInclude(u => u.Avatar)
            .Include(cm => cm.Channel)
                .ThenInclude(c => c.Icon)
            .FirstOrDefaultAsync(cm => cm.ChannelId == channelId && cm.UserId == userId && cm.LeftAt == null, ct);
    }

    public async Task<ChannelMember?> GetMemberByIdAsync(Guid memberId, CancellationToken ct = default)
    {
        return await _db.ChannelMembers
            .Include(cm => cm.User)
                .ThenInclude(u => u.Avatar)
            .Include(cm => cm.AssignedRoles)
                .ThenInclude(ar => ar.Role)
            .Include(cm => cm.Channel)
            .FirstOrDefaultAsync(cm => cm.ChannelMemberId == memberId, ct);
    }

    public async Task<(IReadOnlyList<ChannelMember> Items, int TotalCount)> GetMembersPagedAsync(
        Guid channelId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.ChannelMembers
            .Where(cm => cm.ChannelId == channelId && cm.LeftAt == null)
            .OrderBy(cm => cm.JoinedAt);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(cm => cm.User)
                .ThenInclude(u => u.Avatar)
            .Include(cm => cm.AssignedRoles)
                .ThenInclude(ar => ar.Role)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<ChannelRole>> GetChannelRolesByIdsAsync(
        Guid channelId, IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        return await _db.ChannelRoles
            .Where(r => r.ChannelId == channelId && roleIds.Contains(r.ChannelRoleId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ChannelMemberRole>> GetMemberRolesAsync(
        Guid channelMemberId, CancellationToken ct = default)
    {
        return await _db.ChannelMemberRoles
            .Where(mr => mr.ChannelMemberId == channelMemberId)
            .ToListAsync(ct);
    }

    public async Task<Attachment?> GetAttachmentByIdAsync(Guid attachmentId, CancellationToken ct = default)
    {
        return await _db.Attachments
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct);
    }

    public async Task<bool> HasActiveBanAsync(Guid channelId, string userId, CancellationToken ct = default)
    {
        return await _db.ModerationActions
            .IgnoreQueryFilters()
            .AnyAsync(ma =>
                ma.ChannelId == channelId &&
                ma.TargetUserId == userId &&
                ma.Type == ModerationType.Ban &&
                ma.IsActive, ct);
    }

    public async Task<int> GetUnreadCountAsync(Guid channelId, string userId, DateTimeOffset? lastReadAt, CancellationToken ct = default)
    {
        var query = _db.Messages
            .Where(m => m.ChannelId == channelId && m.AuthorUserId != userId);

        if (lastReadAt.HasValue)
            query = query.Where(m => m.SentAt > lastReadAt.Value);

        return await query.CountAsync(ct);
    }

    public async Task AddChannelAsync(Channel channel, CancellationToken ct = default)
    {
        await _db.Channels.AddAsync(channel, ct);
    }

    public async Task AddMemberAsync(ChannelMember member, CancellationToken ct = default)
    {
        await _db.ChannelMembers.AddAsync(member, ct);
    }

    public async Task AddRoleAsync(ChannelRole role, CancellationToken ct = default)
    {
        await _db.ChannelRoles.AddAsync(role, ct);
    }

    public async Task AddMemberRoleAsync(ChannelMemberRole memberRole, CancellationToken ct = default)
    {
        await _db.ChannelMemberRoles.AddAsync(memberRole, ct);
    }

    public void RemoveMemberRoleAsync(ChannelMemberRole memberRole)
    {
        _db.ChannelMemberRoles.Remove(memberRole);
    }

    public async Task<ChannelRole?> GetSystemRoleAsync(Guid channelId, string roleName, CancellationToken ct = default)
    {
        return await _db.ChannelRoles
            .FirstOrDefaultAsync(r => r.ChannelId == channelId && r.Name == roleName && r.IsSystemRole, ct);
    }

    public async Task<ChannelMemberRole?> GetMemberRoleAssignmentAsync(Guid channelMemberId, Guid channelRoleId, CancellationToken ct = default)
    {
        return await _db.ChannelMemberRoles
            .FirstOrDefaultAsync(mr => mr.ChannelMemberId == channelMemberId && mr.ChannelRoleId == channelRoleId, ct);
    }

    public async Task AddAuditLogAsync(ChannelAuditLog auditLog, CancellationToken ct = default)
    {
        await _db.ChannelAuditLogs.AddAsync(auditLog, ct);
    }

    private const string InviteCodeIndexName = "IX_Channels_InviteCode";
    private const string MemberUniqueIndexName = "IX_ChannelMembers_ChannelId_UserId";

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("Channel", "concurrent modification");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains(InviteCodeIndexName, StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new DuplicateException("Channel", "InviteCode", "(generated)");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains(MemberUniqueIndexName, StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new DuplicateException("ChannelMember", "ChannelId+UserId", "(duplicate)");
        }
    }
}
