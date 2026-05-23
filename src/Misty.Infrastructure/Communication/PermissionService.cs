using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

// Permissions are currently checked directly from the database. Redis caching will be added later
public sealed class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _db;

    public PermissionService(ApplicationDbContext db) => _db = db;

    public async Task<bool> CheckPermissionAsync(
        Guid userId,
        Guid channelId,
        ChannelPermission permission,
        CancellationToken ct = default)
    {
        var utcNow = DateTime.UtcNow;

        // A banned user is denied all permissions.
        var isBanned = await _db.ModerationActions.AnyAsync(
            m => m.ChannelId == channelId
              && m.TargetUserId == userId
              && m.Type == ModerationActionType.Ban
              && m.RevokedAt == null
              && (m.ExpiresAt == null || m.ExpiresAt > utcNow),
            ct);

        if (isBanned)
            return false;

        // A muted user is denied write-class permissions.
        var writeMask = ChannelPermission.SendMessages
                      | ChannelPermission.AttachFiles
                      | ChannelPermission.AddReactions
                      | ChannelPermission.MentionEveryone;

        if ((permission & writeMask) != 0)
        {
            var isMuted = await _db.ModerationActions.AnyAsync(
                m => m.ChannelId == channelId
                  && m.TargetUserId == userId
                  && m.Type == ModerationActionType.Mute
                  && m.RevokedAt == null
                  && (m.ExpiresAt == null || m.ExpiresAt > utcNow),
                ct);

            if (isMuted)
                return false;
        }

        // Resolve permissions from the user's membership and assigned roles.
        var membership = await _db.Memberships
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);

        if (membership is null)
            return false;

        // Aggregate flags from all roles assigned to this membership.
        var effective = await _db.MemberRoles
            .AsNoTracking()
            .Where(mr => mr.MembershipId == membership.Id)
            .Join(_db.ChannelRoles.AsNoTracking(),
                mr => mr.RoleId,
                cr => cr.Id,
                (mr, cr) => cr.Permissions)
            .ToListAsync(ct);

        var aggregated = effective.Aggregate(ChannelPermission.None, (acc, p) => acc | p);

        return (aggregated & permission) == permission;
    }
}
