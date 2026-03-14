namespace Misty.Application.DTOs;

public record UserBlockResponse
{
    public Guid UserBlockId { get; init; }
    public required UserSummary BlockedUser { get; init; }
    public DateTimeOffset BlockedAt { get; init; }
}

public record CreateUserBlockRequest
{
    public string BlockedUserId { get; init; } = default!;
}
