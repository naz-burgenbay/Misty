using Misty.Application.DTOs;

namespace Misty.Application.Interfaces;

public interface IUserService
{
    Task<UserProfileResponse> CreateUserProfileAsync(string identityUserId, string username, string displayName, CancellationToken ct = default);
    Task<UserProfileResponse> GetProfileAsync(string userId, CancellationToken ct = default);
    Task<CurrentUserResponse> GetCurrentUserAsync(string userId, CancellationToken ct = default);
    Task<UserProfileResponse> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken ct = default);

    Task SetAvatarAsync(string userId, Guid attachmentId, byte[] version, CancellationToken ct = default);
    Task RemoveAvatarAsync(string userId, byte[] version, CancellationToken ct = default);

    /// Anonymizes all PII. Fails if the user still owns any channels.
    Task DeleteAccountAsync(string userId, CancellationToken ct = default);
}
