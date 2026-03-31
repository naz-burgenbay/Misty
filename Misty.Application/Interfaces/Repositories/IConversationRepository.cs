using Misty.Domain.Entities;

namespace Misty.Application.Interfaces;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Guid conversationId, CancellationToken ct = default);
    Task<Conversation?> GetByParticipantsAsync(string userId, string otherUserId, CancellationToken ct = default);
    Task<IReadOnlyList<Conversation>> GetVisibleConversationsAsync(string userId, CancellationToken ct = default);
    Task<ConversationParticipant?> GetParticipantAsync(Guid conversationId, string userId, CancellationToken ct = default);
    Task<int> GetUnreadCountAsync(Guid conversationId, string userId, DateTimeOffset? lastReadAt, CancellationToken ct = default);
    Task CreateConversationAsync(Conversation conversation, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
