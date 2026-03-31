using FluentValidation;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IIdentityService _identityService;
    private readonly IBlobStorageProvider _blobStorage;
    private readonly IValidator<UpdateProfileRequest> _updateProfileValidator;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IIdentityService identityService,
        IBlobStorageProvider blobStorage,
        IValidator<UpdateProfileRequest> updateProfileValidator,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _identityService = identityService;
        _blobStorage = blobStorage;
        _updateProfileValidator = updateProfileValidator;
        _logger = logger;
    }

    // UC-1.1 Create User Profile
    public async Task<UserProfileResponse> CreateUserProfileAsync(
        string identityUserId, string username, string displayName, CancellationToken ct = default)
    {
        var existing = await _userRepository.GetByIdAsync(identityUserId, ct);
        if (existing is not null)
            throw new DuplicateException("User", "IdentityUserId", identityUserId);

        var user = new User
        {
            UserId = identityUserId,
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _userRepository.AddAsync(user, ct);
        await _userRepository.SaveChangesAsync(ct);

        _logger.LogInformation("User profile created for {UserId}", user.UserId);

        return ToProfileResponse(user, avatarUrl: null);
    }

    // UC-1.2 Get Current User
    public async Task<CurrentUserResponse> GetCurrentUserAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("User", userId);

        var email = await _identityService.GetEmailAsync(userId, ct)
            ?? throw new NotFoundException("Identity user", userId);

        var avatarUrl = await GetAvatarUrlAsync(user, ct);

        return new CurrentUserResponse
        {
            Id = user.UserId,
            DisplayName = user.DisplayName,
            Bio = user.Bio,
            AvatarUrl = avatarUrl,
            Email = email,
            CreatedAt = user.CreatedAt
        };
    }

    // UC-1.3 Get User Profile
    public async Task<UserProfileResponse> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("User", userId);

        var avatarUrl = await GetAvatarUrlAsync(user, ct);

        return ToProfileResponse(user, avatarUrl);
    }

    // UC-1.4 Update Profile
    public async Task<UserProfileResponse> UpdateProfileAsync(
        string userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        await _updateProfileValidator.ValidateAndThrowAsync(request, ct);

        var user = await _userRepository.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("User", userId);

        if (request.DisplayName is not null)
            user.DisplayName = request.DisplayName;

        if (request.Bio is not null)
            user.Bio = request.Bio;

        user.Version = request.Version;
        await _userRepository.SaveChangesAsync(ct);

        _logger.LogInformation("User profile updated for {UserId}", userId);

        var avatarUrl = await GetAvatarUrlAsync(user, ct);
        return ToProfileResponse(user, avatarUrl);
    }

    // UC-1.5 Set Avatar
    public async Task SetAvatarAsync(
        string userId, Guid attachmentId, byte[] version, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("User", userId);

        var attachment = await _userRepository.GetAttachmentByIdAsync(attachmentId, ct)
            ?? throw new NotFoundException("Attachment", attachmentId);

        if (attachment.UploadedByUserId != userId || attachment.Purpose != AttachmentPurpose.UserAvatar)
            throw new BusinessRuleException("Attachment is not a user avatar owned by the caller.");

        user.AvatarAttachmentId = attachmentId;
        user.Version = version;
        await _userRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Avatar set for {UserId}", userId);
    }

    // UC-1.6 Remove Avatar
    public async Task RemoveAvatarAsync(string userId, byte[] version, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("User", userId);

        user.AvatarAttachmentId = null;
        user.Version = version;
        await _userRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Avatar removed for {UserId}", userId);
    }

    // UC-1.7 Delete Account
    public async Task DeleteAccountAsync(string userId, CancellationToken ct = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException("User", userId);

        if (await _userRepository.OwnsAnyChannelsAsync(userId, ct))
            throw new BusinessRuleException(
                "User must transfer or delete all owned channels before deleting their account.");

        // Capture avatar info before scrubbing so we can delete the blob after commit
        var avatarStoragePath = user.Avatar?.StoragePath;

        // Delete avatar attachment row
        if (user.Avatar is not null)
            await _userRepository.DeleteAttachmentAsync(user.Avatar.AttachmentId, ct);

        // Scrub domain user record
        var token = Guid.NewGuid().ToString("N");
        user.Username = $"deleted_{token}";
        user.NormalizedUsername = $"DELETED_{token}";
        user.DisplayName = "Deleted User";
        user.Bio = null;
        user.AvatarAttachmentId = null;
        user.DeletedAt = DateTimeOffset.UtcNow;

        // Bulk cleanup
        var now = DateTimeOffset.UtcNow;
        await _userRepository.DeleteAllUserBlocksAsync(userId, ct);
        await _userRepository.DeleteAllReactionsAsync(userId, ct);
        await _userRepository.DeactivateAllMembershipsAsync(userId, now, ct);
        await _userRepository.ScrubAuditLogIpAddressesAsync(userId, ct);

        // Persist all DB changes before external side-effects
        await _userRepository.SaveChangesAsync(ct);

        // External side-effects AFTER commit
        try
        {
            await _identityService.AnonymizeIdentityAsync(userId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to anonymize identity for {UserId}", userId);
        }

        if (avatarStoragePath is not null)
        {
            try
            {
                await _blobStorage.DeleteAsync(avatarStoragePath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete avatar blob for {UserId} at {Path}", userId, avatarStoragePath);
            }
        }

        _logger.LogInformation("Account deleted (anonymized) for {UserId}", userId);
    }

    private async Task<string?> GetAvatarUrlAsync(User user, CancellationToken ct)
    {
        if (user.Avatar is null) return null;
        return await _blobStorage.GetDownloadUrlAsync(user.Avatar.StoragePath, ct);
    }

    private static UserProfileResponse ToProfileResponse(User user, string? avatarUrl) => new()
    {
        Id = user.UserId,
        DisplayName = user.DisplayName,
        Bio = user.Bio,
        AvatarUrl = avatarUrl,
        CreatedAt = user.CreatedAt
    };
}
