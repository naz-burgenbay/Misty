using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;

namespace Misty.Infrastructure.Communication;

// Temporary placeholder until the actual SQL/cache-backed implementation is added later. For now, all permission checks return false as a safe default during Phase 3 development.
public sealed class StubPermissionService : IPermissionService
{
    public Task<bool> CheckPermissionAsync(
        Guid userId,
        Guid channelId,
        ChannelPermission permission,
        CancellationToken ct = default)
        => Task.FromResult(false);
}
