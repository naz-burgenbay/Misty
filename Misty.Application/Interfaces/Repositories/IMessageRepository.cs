using Misty.Domain.Entities;

namespace Misty.Application.Interfaces;

public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(Guid messageId, CancellationToken ct = default);
    Task<Message?> GetByIdempotencyKeyAsync(string authorUserId, string idempotencyKey, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetChannelMessagesAsync(
        Guid channelId, int limit, DateTimeOffset? cursorSentAt, Guid? cursorMessageId, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetConversationMessagesAsync(
        Guid conversationId, int limit, DateTimeOffset? cursorSentAt, Guid? cursorMessageId, CancellationToken ct = default);
    Task<IReadOnlyList<Message>> GetRepliesAsync(Guid parentMessageId, CancellationToken ct = default);
    Task AddAsync(Message message, CancellationToken ct = default);
    Task DeleteAsync(Message message);
    Task<MessageReaction?> GetReactionAsync(Guid messageId, string userId, string emoji, CancellationToken ct = default);
    Task AddReactionAsync(MessageReaction reaction, CancellationToken ct = default);
    Task DeleteReactionAsync(MessageReaction reaction);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<bool> IsUserMutedAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task<ChannelMember?> GetChannelMemberAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task<ConversationParticipant?> GetConversationParticipantAsync(Guid conversationId, string userId, CancellationToken ct = default);
    Task<IReadOnlyList<Attachment>> GetAttachmentsByIdsAsync(IEnumerable<Guid> attachmentIds, CancellationToken ct = default);
    Task<int> ClaimAttachmentsAsync(IReadOnlyList<Guid> attachmentIds, Guid messageId, CancellationToken ct = default);
    Task<ConversationParticipant?> GetOtherConversationParticipantAsync(Guid conversationId, string userId, CancellationToken ct = default);
    Task<Conversation?> GetConversationAsync(Guid conversationId, CancellationToken ct = default);
    Task AddAuditLogAsync(ChannelAuditLog auditLog, CancellationToken ct = default);
}
