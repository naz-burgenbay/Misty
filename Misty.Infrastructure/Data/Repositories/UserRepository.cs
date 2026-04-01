using Microsoft.EntityFrameworkCore;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;

namespace Misty.Infrastructure.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _db;

    public UserRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<User?> GetByIdAsync(string userId, CancellationToken ct = default)
    {
        return await _db.DomainUsers
            .Include(u => u.Avatar)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct);
    }

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await _db.DomainUsers.AddAsync(user, ct);
    }

    public async Task<Attachment?> GetAttachmentByIdAsync(Guid attachmentId, CancellationToken ct = default)
    {
        return await _db.Attachments.FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct);
    }

    public async Task<bool> OwnsAnyChannelsAsync(string userId, CancellationToken ct = default)
    {
        return await _db.Channels.AnyAsync(c => c.OwnerUserId == userId, ct);
    }

    // User Blocking (UC-9)

    public async Task<UserBlock?> GetBlockByIdAsync(Guid blockId, CancellationToken ct = default)
    {
        return await _db.UserBlocks.FirstOrDefaultAsync(b => b.UserBlockId == blockId, ct);
    }

    public async Task<IReadOnlyList<UserBlock>> GetBlocksByUserAsync(string userId, CancellationToken ct = default)
    {
        return await _db.UserBlocks
            .Where(b => b.BlockingUserId == userId)
            .Include(b => b.BlockedUser)
                .ThenInclude(u => u.Avatar)
            .OrderByDescending(b => b.BlockedAt)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsBlockBetweenAsync(string userId, string otherUserId, CancellationToken ct = default)
    {
        return await _db.UserBlocks.AnyAsync(b =>
            (b.BlockingUserId == userId && b.BlockedUserId == otherUserId) ||
            (b.BlockingUserId == otherUserId && b.BlockedUserId == userId), ct);
    }

    public async Task AddBlockAsync(UserBlock block, CancellationToken ct = default)
    {
        await _db.UserBlocks.AddAsync(block, ct);
    }

    public void RemoveBlock(UserBlock block)
    {
        _db.UserBlocks.Remove(block);
    }

    // Account-deletion bulk (UC-1.7)

    public async Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken ct = default)
    {
        await _db.Attachments
            .Where(a => a.AttachmentId == attachmentId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task DeleteAllUserBlocksAsync(string userId, CancellationToken ct = default)
    {
        await _db.UserBlocks
            .Where(b => b.BlockingUserId == userId || b.BlockedUserId == userId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task DeleteAllReactionsAsync(string userId, CancellationToken ct = default)
    {
        await _db.MessageReactions
            .Where(r => r.ReactedByUserId == userId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task DeactivateAllMembershipsAsync(string userId, DateTimeOffset leftAt, CancellationToken ct = default)
    {
        await _db.ChannelMembers
            .Where(cm => cm.UserId == userId && cm.LeftAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(cm => cm.LeftAt, leftAt), ct);
    }

    public async Task ScrubAuditLogIpAddressesAsync(string userId, CancellationToken ct = default)
    {
        await _db.ChannelAuditLogs
            .Where(log => log.ActorUserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(log => log.IpAddress, (string?)null), ct);
    }

    private const string BlockUniqueIndexName = "IX_UserBlocks_BlockingUserId_BlockedUserId";

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException("User", "concurrent modification");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains(BlockUniqueIndexName, StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new DuplicateException("UserBlock", "BlockingUserId+BlockedUserId", "(duplicate)");
        }
    }
}
