using Microsoft.EntityFrameworkCore;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Infrastructure.Data.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly ApplicationDbContext _db;

    private const string IdempotencyIndexName = "IX_Messages_AuthorUserId_IdempotencyKey";
    private const string ReactionUniqueIndexName = "IX_MessageReactions_MessageId_ReactedByUserId_Emoji";

    public MessageRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Message?> GetByIdAsync(Guid messageId, CancellationToken ct = default)
    {
        return await _db.Messages
            .Include(m => m.Author)
                .ThenInclude(u => u.Avatar)
            .Include(m => m.Attachments)
            .Include(m => m.Reactions)
            .Include(m => m.ParentMessage)
                .ThenInclude(pm => pm!.Author)
            .FirstOrDefaultAsync(m => m.MessageId == messageId, ct);
    }

    public async Task<Message?> GetByIdempotencyKeyAsync(string authorUserId, string idempotencyKey, CancellationToken ct = default)
    {
        return await _db.Messages
            .Include(m => m.Author)
                .ThenInclude(u => u.Avatar)
            .Include(m => m.Attachments)
            .Include(m => m.Reactions)
            .Include(m => m.ParentMessage)
                .ThenInclude(pm => pm!.Author)
            .FirstOrDefaultAsync(m => m.AuthorUserId == authorUserId && m.IdempotencyKey == idempotencyKey, ct);
    }

    public async Task<IReadOnlyList<Message>> GetChannelMessagesAsync(
        Guid channelId, int limit, DateTimeOffset? cursorSentAt, Guid? cursorMessageId, CancellationToken ct = default)
    {
        var query = _db.Messages
            .Where(m => m.ChannelId == channelId);

        if (cursorSentAt.HasValue && cursorMessageId.HasValue)
        {
            query = query.Where(m =>
                m.SentAt < cursorSentAt.Value ||
                (m.SentAt == cursorSentAt.Value && m.MessageId.CompareTo(cursorMessageId.Value) < 0));
        }

        return await query
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.MessageId)
            .Take(limit)
            .Include(m => m.Author)
                .ThenInclude(u => u.Avatar)
            .Include(m => m.Attachments)
            .Include(m => m.Reactions)
            .Include(m => m.ParentMessage)
                .ThenInclude(pm => pm!.Author)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Message>> GetConversationMessagesAsync(
        Guid conversationId, int limit, DateTimeOffset? cursorSentAt, Guid? cursorMessageId, CancellationToken ct = default)
    {
        var query = _db.Messages
            .Where(m => m.ConversationId == conversationId);

        if (cursorSentAt.HasValue && cursorMessageId.HasValue)
        {
            query = query.Where(m =>
                m.SentAt < cursorSentAt.Value ||
                (m.SentAt == cursorSentAt.Value && m.MessageId.CompareTo(cursorMessageId.Value) < 0));
        }

        return await query
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.MessageId)
            .Take(limit)
            .Include(m => m.Author)
                .ThenInclude(u => u.Avatar)
            .Include(m => m.Attachments)
            .Include(m => m.Reactions)
            .Include(m => m.ParentMessage)
                .ThenInclude(pm => pm!.Author)
            .AsSplitQuery()
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Message>> GetRepliesAsync(Guid parentMessageId, CancellationToken ct = default)
    {
        return await _db.Messages
            .Where(m => m.ParentMessageId == parentMessageId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Message message, CancellationToken ct = default)
    {
        await _db.Messages.AddAsync(message, ct);
    }

    public Task DeleteAsync(Message message)
    {
        _db.Messages.Remove(message);
        return Task.CompletedTask;
    }

    public async Task<MessageReaction?> GetReactionAsync(Guid messageId, string userId, string emoji, CancellationToken ct = default)
    {
        return await _db.MessageReactions
            .FirstOrDefaultAsync(r => r.MessageId == messageId && r.ReactedByUserId == userId && r.Emoji == emoji, ct);
    }

    public async Task AddReactionAsync(MessageReaction reaction, CancellationToken ct = default)
    {
        await _db.MessageReactions.AddAsync(reaction, ct);
    }

    public Task DeleteReactionAsync(MessageReaction reaction)
    {
        _db.MessageReactions.Remove(reaction);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsIdempotencyViolation(ex))
        {
            throw new DuplicateException("Message", "IdempotencyKey", "duplicate");
        }
        catch (DbUpdateException ex) when (IsReactionUniqueViolation(ex))
        {
            throw new DuplicateException("MessageReaction", "MessageId+UserId+Emoji", "duplicate");
        }
    }

    public async Task<bool> IsUserMutedAsync(Guid channelId, string userId, CancellationToken ct = default)
    {
        return await _db.ModerationActions
            .IgnoreQueryFilters()
            .AnyAsync(ma =>
                ma.ChannelId == channelId &&
                ma.TargetUserId == userId &&
                ma.Type == ModerationType.Mute &&
                ma.IsActive, ct);
    }

    public async Task<ChannelMember?> GetChannelMemberAsync(Guid channelId, string userId, CancellationToken ct = default)
    {
        return await _db.ChannelMembers
            .Include(cm => cm.AssignedRoles)
                .ThenInclude(ar => ar.Role)
            .Include(cm => cm.Channel)
            .FirstOrDefaultAsync(cm => cm.ChannelId == channelId && cm.UserId == userId && cm.LeftAt == null, ct);
    }

    public async Task<ConversationParticipant?> GetConversationParticipantAsync(
        Guid conversationId, string userId, CancellationToken ct = default)
    {
        return await _db.ConversationParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == conversationId && p.UserId == userId, ct);
    }

    public async Task<IReadOnlyList<Attachment>> GetAttachmentsByIdsAsync(IEnumerable<Guid> attachmentIds, CancellationToken ct = default)
    {
        var ids = attachmentIds.ToList();
        return await _db.Attachments
            .Where(a => ids.Contains(a.AttachmentId))
            .ToListAsync(ct);
    }

    public async Task<int> ClaimAttachmentsAsync(IReadOnlyList<Guid> attachmentIds, Guid messageId, CancellationToken ct = default)
    {
        return await _db.Attachments
            .Where(a => attachmentIds.Contains(a.AttachmentId) && a.MessageId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.MessageId, messageId), ct);
    }

    public async Task<ConversationParticipant?> GetOtherConversationParticipantAsync(
        Guid conversationId, string userId, CancellationToken ct = default)
    {
        return await _db.ConversationParticipants
            .FirstOrDefaultAsync(p => p.ConversationId == conversationId && p.UserId != userId, ct);
    }

    public async Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken ct = default)
    {
        return await _db.Conversations
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId, ct);
    }

    public async Task AddAuditLogAsync(ChannelAuditLog auditLog, CancellationToken ct = default)
    {
        await _db.ChannelAuditLogs.AddAsync(auditLog, ct);
    }

    private static bool IsIdempotencyViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains(IdempotencyIndexName, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsReactionUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException?.Message.Contains(ReactionUniqueIndexName, StringComparison.OrdinalIgnoreCase) == true;
    }
}
