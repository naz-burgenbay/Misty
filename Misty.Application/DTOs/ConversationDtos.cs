namespace Misty.Application.DTOs;

public record ConversationSummary
{
    public Guid ConversationId { get; init; }
    public required UserSummary OtherParticipant { get; init; }
    public MessageSummary? LastMessage { get; init; }
    public int UnreadCount { get; init; }
}

public record ConversationDetailResponse
{
    public Guid ConversationId { get; init; }
    public required UserSummary OtherParticipant { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
