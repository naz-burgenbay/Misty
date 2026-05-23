using Misty.Application.Communication.Contracts;

namespace Misty.Infrastructure.Communication;

// No-op stub used until the real SQL implementation is wired in later.
public sealed class StubUserQueryService : IUserQueryService
{
    public Task<UserSummary?> GetByIdAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<UserSummary?>(null);

    public Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(false);
}
