using Microsoft.EntityFrameworkCore;
using Misty.Application.Messaging;
using Misty.Domain.Messaging;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Messaging;

public sealed class MessageRepository : IMessageRepository
{
    private readonly ApplicationDbContext _db;

    public MessageRepository(ApplicationDbContext db) => _db = db;

    public Task<Message?> FindByIdempotencyKeyAsync(Guid authorId, string idempotencyKey, CancellationToken ct = default)
        => _db.Messages.FirstOrDefaultAsync(
            m => m.AuthorId == authorId && m.IdempotencyKey == idempotencyKey, ct);

    public async Task AddAsync(Message message, CancellationToken ct = default)
    {
        await _db.Messages.AddAsync(message, ct);
        await _db.SaveChangesAsync(ct);
    }
}
