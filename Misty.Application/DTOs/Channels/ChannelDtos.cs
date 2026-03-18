using Misty.Application.Enums;

namespace Misty.Application.DTOs.Channels;

public record ChannelSummary
{
    public Guid ChannelId { get; init; }
    public required string Name { get; init; }
    public string? IconUrl { get; init; }
    public bool IsPrivate { get; init; }
    public int MemberCount { get; init; }
    public DateTimeOffset? LastMessageAt { get; init; }
    public int UnreadCount { get; init; }
}

public record ChannelDetailResponse
{
    public Guid ChannelId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? IconUrl { get; init; }
    public bool IsPrivate { get; init; }
    public bool IsAiAssistantEnabled { get; init; }
    public int MemberCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public required UserSummary Owner { get; init; }
    public string? InviteCode { get; init; }
    public ChannelPermission MyPermissions { get; init; }
    public ChannelPermission DefaultPermissions { get; init; }
}

public record CreateChannelRequest
{
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
    public bool IsPrivate { get; init; }
}

public record UpdateChannelRequest
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public bool? IsPrivate { get; init; }
    public bool? IsAiAssistantEnabled { get; init; }
    public ChannelPermission? DefaultPermissions { get; init; }
}

public record TransferOwnershipRequest
{
    public string NewOwnerUserId { get; init; } = default!;
}
