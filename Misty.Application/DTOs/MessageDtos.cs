namespace Misty.Application.DTOs;

public record MessageResponse
{
    public Guid MessageId { get; init; }
    public required UserSummary Author { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset SentAt { get; init; }
    public bool IsEdited { get; init; }
    public bool IsReply { get; init; }
    public ParentMessagePreview? ParentMessage { get; init; }
    public IReadOnlyList<AttachmentResponse> Attachments { get; init; } = [];
    public IReadOnlyList<ReactionGroup> Reactions { get; init; } = [];
}

public record MessageSummary
{
    public Guid MessageId { get; init; }
    public required string AuthorDisplayName { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset SentAt { get; init; }
}

public record ParentMessagePreview
{
    public Guid MessageId { get; init; }
    public required string AuthorDisplayName { get; init; }
    public required string Content { get; init; }
}

public record SendMessageRequest
{
    public string Content { get; init; } = default!;
    public Guid? ParentMessageId { get; init; }
}

public record UpdateMessageRequest
{
    public string Content { get; init; } = default!;
}

public record ReactionGroup
{
    public required string Emoji { get; init; }
    public int Count { get; init; }
    public bool ReactedByMe { get; init; }
}

public record AddReactionRequest
{
    public string Emoji { get; init; } = default!;
}
