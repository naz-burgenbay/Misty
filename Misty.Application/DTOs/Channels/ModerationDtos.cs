using Misty.Application.Enums;

namespace Misty.Application.DTOs.Channels;

public record ModerationActionResponse
{
    public Guid ModerationActionId { get; init; }
    public ModerationType Type { get; init; }
    public required UserSummary TargetUser { get; init; }
    public required string Reason { get; init; }
    public DateTimeOffset StartAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool IsActive { get; init; }
    public required UserSummary CreatedBy { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
    public UserSummary? UpdatedBy { get; init; }
}

public record ModerationActionSummary
{
    public Guid ModerationActionId { get; init; }
    public ModerationType Type { get; init; }
    public required string TargetUserDisplayName { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset StartAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record CreateModerationActionRequest
{
    public string TargetUserId { get; init; } = default!;
    public ModerationType Type { get; init; }
    public string Reason { get; init; } = default!;
    public DateTimeOffset? ExpiresAt { get; init; }
}

public record RevokeModerationActionRequest
{
    public string? Reason { get; init; }
}
