using Misty.Application.DTOs;

namespace Misty.Application.Interfaces;

public interface IConversationService
{
    /// Returns the existing conversation between two users, or creates one if none exists.
    Task<ConversationDetailResponse> GetOrCreateConversationAsync(string userId, string otherUserId, CancellationToken ct = default);

    Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(string userId, CancellationToken ct = default);
    Task<ConversationDetailResponse> GetConversationAsync(Guid conversationId, string userId, CancellationToken ct = default);

    /// Hides the conversation from the user's inbox without deleting it. A new message from the other participant will resurface it.
    Task HideConversationAsync(Guid conversationId, string userId, CancellationToken ct = default);

    Task MarkConversationReadAsync(Guid conversationId, string userId, DateTimeOffset lastReadAt, CancellationToken ct = default);
}
