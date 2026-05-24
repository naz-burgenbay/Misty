using Misty.Domain.Messaging;

namespace Misty.Application.Messaging;

public interface IMessageRepository
{
    Task<Message?> FindByIdempotencyKeyAsync(Guid authorId, string idempotencyKey, CancellationToken ct = default);
    Task AddAsync(Message message, CancellationToken ct = default);
}
