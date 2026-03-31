using Microsoft.EntityFrameworkCore;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;

namespace Misty.Infrastructure.Data.Repositories;

public class ConversationRepository : IConversationRepository
{
    private readonly ApplicationDbContext _db;

    public ConversationRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Conversation?> GetByIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        return await _db.Conversations
            .Include(c => c.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.Avatar)
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId, ct);
    }

    public async Task<Conversation?> GetByParticipantsAsync(string userId, string otherUserId, CancellationToken ct = default)
    {
        var (low, high) = Conversation.NormalizeParticipants(userId, otherUserId);

        return await _db.Conversations
            .Include(c => c.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.Avatar)
            .FirstOrDefaultAsync(c =>
                c.ParticipantLowUserId == low && c.ParticipantHighUserId == high, ct);
    }

    public async Task<IReadOnlyList<Conversation>> GetVisibleConversationsAsync(string userId, CancellationToken ct = default)
    {
        return await _db.Conversations
            .Include(c => c.Participants)
                .ThenInclude(p => p.User)
                    .ThenInclude(u => u.Avatar)
            .Include(c => c.Messages.OrderByDescending(m => m.SentAt).Take(1))
                .ThenInclude(m => m.Author)
            .Where(c => c.Participants.Any(p => p.UserId == userId && p.HiddenAt == null))
            .OrderByDescending(c => c.LastMessageAt)
            .ToListAsync(ct);
    }

    public async Task<ConversationParticipant?> GetParticipantAsync(Guid conversationId, string userId, CancellationToken ct = default)
    {
        return await _db.ConversationParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == conversationId && p.UserId == userId, ct);
    }

    public async Task<int> GetUnreadCountAsync(Guid conversationId, string userId, DateTimeOffset? lastReadAt, CancellationToken ct = default)
    {
        var query = _db.Messages
            .Where(m => m.ConversationId == conversationId && m.AuthorUserId != userId);

        if (lastReadAt.HasValue)
            query = query.Where(m => m.SentAt > lastReadAt.Value);

        return await query.CountAsync(ct);
    }

    private const string ParticipantPairIndexName =
        "IX_Conversations_ParticipantLowUserId_ParticipantHighUserId";

    public async Task CreateConversationAsync(Conversation conversation, CancellationToken ct = default)
    {
        await _db.Conversations.AddAsync(conversation, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsParticipantPairViolation(ex))
        {
            _db.Entry(conversation).State = EntityState.Detached;
            foreach (var participant in conversation.Participants)
                _db.Entry(participant).State = EntityState.Detached;

            throw new DuplicateException("Conversation", "Participants",
                $"{conversation.ParticipantLowUserId},{conversation.ParticipantHighUserId}");
        }
    }

    private static bool IsParticipantPairViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains(ParticipantPairIndexName, StringComparison.OrdinalIgnoreCase)
            == true;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
    }
}
