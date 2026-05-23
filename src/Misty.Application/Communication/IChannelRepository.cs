using Misty.Domain.Communication;

namespace Misty.Application.Communication;

public interface IChannelRepository
{
    Task<Channel?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Channel channel, CancellationToken ct = default);
    Task UpdateAsync(Channel channel, byte[] concurrencyToken, CancellationToken ct = default);
    Task SoftDeleteAsync(Channel channel, CancellationToken ct = default);
}
