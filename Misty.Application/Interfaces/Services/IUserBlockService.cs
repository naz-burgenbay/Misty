using Misty.Application.DTOs;

namespace Misty.Application.Interfaces;

public interface IUserBlockService
{
    Task<IReadOnlyList<UserBlockResponse>> GetBlockedUsersAsync(string userId, CancellationToken ct = default);
    Task<UserBlockResponse> BlockUserAsync(string userId, CreateUserBlockRequest request, CancellationToken ct = default);
    Task UnblockUserAsync(string userId, Guid blockId, CancellationToken ct = default);
    Task EnsureNotBlockedAsync(string userId, string otherUserId, CancellationToken ct = default);
}
