using Misty.Domain.Entities;

namespace Misty.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(string userId, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);
    Task<Attachment?> GetAttachmentByIdAsync(Guid attachmentId, CancellationToken ct = default);
    Task<bool> OwnsAnyChannelsAsync(string userId, CancellationToken ct = default);

    // User Blocking (UC-9)
    Task<UserBlock?> GetBlockByIdAsync(Guid blockId, CancellationToken ct = default);
    Task<IReadOnlyList<UserBlock>> GetBlocksByUserAsync(string userId, CancellationToken ct = default);
    Task<bool> ExistsBlockBetweenAsync(string userId, string otherUserId, CancellationToken ct = default);
    Task AddBlockAsync(UserBlock block, CancellationToken ct = default);
    void RemoveBlock(UserBlock block);

    // Account-deletion bulk (UC-1.7)
    Task DeleteAttachmentAsync(Guid attachmentId, CancellationToken ct = default);
    Task DeleteAllUserBlocksAsync(string userId, CancellationToken ct = default);
    Task DeleteAllReactionsAsync(string userId, CancellationToken ct = default);
    Task DeactivateAllMembershipsAsync(string userId, DateTimeOffset leftAt, CancellationToken ct = default);
    Task ScrubAuditLogIpAddressesAsync(string userId, CancellationToken ct = default);
    
    Task SaveChangesAsync(CancellationToken ct = default);
}
