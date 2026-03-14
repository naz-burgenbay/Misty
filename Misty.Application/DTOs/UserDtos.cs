namespace Misty.Application.DTOs;

public record UserSummary
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
}

public record UserProfileResponse
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Bio { get; init; }
    public string? AvatarUrl { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public record CurrentUserResponse
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public string? Bio { get; init; }
    public string? AvatarUrl { get; init; }
    public required string Email { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public record UpdateProfileRequest
{
    public string? DisplayName { get; init; }
    public string? Bio { get; init; }
}
