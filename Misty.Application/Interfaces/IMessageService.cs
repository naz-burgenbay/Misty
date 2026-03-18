using Misty.Application.DTOs;
using Misty.Application.DTOs.Common;

namespace Misty.Application.Interfaces;

public interface IMessageService
{
    Task<MessageResponse> SendChannelMessageAsync(Guid channelId, string userId, SendMessageRequest request, CancellationToken ct = default);
    Task<MessageResponse> SendConversationMessageAsync(Guid conversationId, string userId, SendMessageRequest request, CancellationToken ct = default);

    Task<CursorPagedResponse<MessageResponse>> GetChannelMessagesAsync(Guid channelId, string userId, int? limit, string? cursor, CancellationToken ct = default);
    Task<CursorPagedResponse<MessageResponse>> GetConversationMessagesAsync(Guid conversationId, string userId, int? limit, string? cursor, CancellationToken ct = default);

    Task<MessageResponse> UpdateMessageAsync(Guid messageId, string userId, UpdateMessageRequest request, CancellationToken ct = default);
    Task DeleteMessageAsync(Guid messageId, string userId, CancellationToken ct = default);

    Task AddReactionAsync(Guid messageId, string userId, AddReactionRequest request, CancellationToken ct = default);
    Task RemoveReactionAsync(Guid messageId, string userId, string emoji, CancellationToken ct = default);
}
