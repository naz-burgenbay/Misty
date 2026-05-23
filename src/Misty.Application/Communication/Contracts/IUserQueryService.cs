namespace Misty.Application.Communication.Contracts;

public sealed record UserSummary(
    Guid Id,
    string Username,
    string DisplayName,
    string? AvatarUrl);

// Cross-module query service that allows non-user modules to look up basic user data without depending directly on the Users infrastructure.
public interface IUserQueryService
{
    Task<UserSummary?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid userId, CancellationToken ct = default);
}
