using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Application.Services;
using Misty.Domain.Entities;
using Misty.Domain.Enums;
using Misty.Tests.Common;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Misty.Tests.Application.Services;

public class UserServiceTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IIdentityService _identityService = Substitute.For<IIdentityService>();
    private readonly IBlobStorageProvider _blobStorage = Substitute.For<IBlobStorageProvider>();
    private readonly IValidator<UpdateProfileRequest> _updateValidator = Substitute.For<IValidator<UpdateProfileRequest>>();
    private readonly UserService _sut;

    private readonly User _user;

    public UserServiceTests()
    {
        _sut = new UserService(
            _userRepo, _identityService, _blobStorage, _updateValidator,
            Substitute.For<ILogger<UserService>>());

        _updateValidator
            .ValidateAsync(Arg.Any<ValidationContext<UpdateProfileRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        _user = TestData.User(displayName: "Alice");
        _userRepo.GetByIdAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(_user);
    }

    // UC-1.1 Create User Profile

    [Fact]
    public async Task CreateProfile_NewUser_CreatesAndReturnsProfile()
    {
        var id = "new-identity-id";
        _userRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _sut.CreateUserProfileAsync(id, "alice", "Alice");

        result.Id.Should().Be(id);
        result.DisplayName.Should().Be("Alice");
        await _userRepo.Received(1).AddAsync(
            Arg.Is<User>(u => u.UserId == id
                && u.Username == "alice"
                && u.NormalizedUsername == "ALICE"
                && u.DisplayName == "Alice"),
            Arg.Any<CancellationToken>());
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateProfile_AlreadyExists_ThrowsDuplicate()
    {
        var act = () => _sut.CreateUserProfileAsync(_user.UserId, "alice", "Alice");

        await act.Should().ThrowAsync<DuplicateException>();
    }

    // UC-1.2 Get Current User

    [Fact]
    public async Task GetCurrentUser_ReturnsEmailAndProfile()
    {
        _identityService.GetEmailAsync(_user.UserId, Arg.Any<CancellationToken>())
            .Returns("alice@example.com");

        var result = await _sut.GetCurrentUserAsync(_user.UserId);

        result.Id.Should().Be(_user.UserId);
        result.DisplayName.Should().Be(_user.DisplayName);
        result.Email.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task GetCurrentUser_WithAvatar_ReturnsAvatarUrl()
    {
        _user.Avatar = CreateAvatar(_user.UserId);
        _identityService.GetEmailAsync(_user.UserId, Arg.Any<CancellationToken>())
            .Returns("alice@example.com");
        _blobStorage.GetDownloadUrlAsync(_user.Avatar.StoragePath, Arg.Any<CancellationToken>())
            .Returns("https://cdn.test/avatar.png");

        var result = await _sut.GetCurrentUserAsync(_user.UserId);

        result.AvatarUrl.Should().Be("https://cdn.test/avatar.png");
    }

    [Fact]
    public async Task GetCurrentUser_NotFound_Throws()
    {
        _userRepo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = () => _sut.GetCurrentUserAsync("missing");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetCurrentUser_IdentityEmailMissing_Throws()
    {
        _identityService.GetEmailAsync(_user.UserId, Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var act = () => _sut.GetCurrentUserAsync(_user.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-1.3 Get User Profile

    [Fact]
    public async Task GetProfile_ReturnsProfile()
    {
        var result = await _sut.GetProfileAsync(_user.UserId);

        result.Id.Should().Be(_user.UserId);
        result.DisplayName.Should().Be(_user.DisplayName);
    }

    [Fact]
    public async Task GetProfile_WithAvatar_ReturnsAvatarUrl()
    {
        _user.Avatar = CreateAvatar(_user.UserId);
        _blobStorage.GetDownloadUrlAsync(_user.Avatar.StoragePath, Arg.Any<CancellationToken>())
            .Returns("https://cdn.test/avatar.png");

        var result = await _sut.GetProfileAsync(_user.UserId);

        result.AvatarUrl.Should().Be("https://cdn.test/avatar.png");
    }

    [Fact]
    public async Task GetProfile_NotFound_Throws()
    {
        _userRepo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = () => _sut.GetProfileAsync("missing");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-1.4 Update Profile

    [Fact]
    public async Task UpdateProfile_ChangesDisplayNameAndBio()
    {
        var request = new UpdateProfileRequest
        {
            DisplayName = "New Name",
            Bio = "New bio",
            Version = _user.Version
        };

        var result = await _sut.UpdateProfileAsync(_user.UserId, request);

        result.DisplayName.Should().Be("New Name");
        result.Bio.Should().Be("New bio");
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateProfile_NullFieldsAreNotOverwritten()
    {
        _user.DisplayName = "Original";
        _user.Bio = "Original bio";
        var request = new UpdateProfileRequest { Version = _user.Version };

        var result = await _sut.UpdateProfileAsync(_user.UserId, request);

        result.DisplayName.Should().Be("Original");
        result.Bio.Should().Be("Original bio");
    }

    [Fact]
    public async Task UpdateProfile_SetsVersionFromRequest()
    {
        var version = new byte[] { 1, 2, 3, 4 };
        var request = new UpdateProfileRequest
        {
            DisplayName = "Changed",
            Version = version
        };

        await _sut.UpdateProfileAsync(_user.UserId, request);

        _user.Version.Should().BeEquivalentTo(version);
    }

    [Fact]
    public async Task UpdateProfile_NotFound_Throws()
    {
        _userRepo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((User?)null);
        var request = new UpdateProfileRequest { Version = [0, 0, 0, 1] };

        var act = () => _sut.UpdateProfileAsync("missing", request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateProfile_ValidationFails_ThrowsValidationException()
    {
        _updateValidator
            .ValidateAsync(Arg.Any<ValidationContext<UpdateProfileRequest>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ValidationException(new[] { new ValidationFailure("DisplayName", "Too long") }));

        var request = new UpdateProfileRequest { DisplayName = new string('x', 200), Version = _user.Version };

        var act = () => _sut.UpdateProfileAsync(_user.UserId, request);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // UC-1.5 Set Avatar

    [Fact]
    public async Task SetAvatar_ValidAttachment_SetsAvatarId()
    {
        var attachment = CreateAvatar(_user.UserId);
        _userRepo.GetAttachmentByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);

        await _sut.SetAvatarAsync(_user.UserId, attachment.AttachmentId, _user.Version);

        _user.AvatarAttachmentId.Should().Be(attachment.AttachmentId);
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAvatar_AttachmentNotFound_Throws()
    {
        var id = Guid.NewGuid();
        _userRepo.GetAttachmentByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Attachment?)null);

        var act = () => _sut.SetAvatarAsync(_user.UserId, id, _user.Version);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SetAvatar_NotOwnedByCaller_Throws()
    {
        var other = TestData.User(displayName: "Other");
        var attachment = CreateAvatar(other.UserId);
        _userRepo.GetAttachmentByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);

        var act = () => _sut.SetAvatarAsync(_user.UserId, attachment.AttachmentId, _user.Version);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SetAvatar_WrongPurpose_Throws()
    {
        var attachment = CreateAvatar(_user.UserId);
        attachment.Purpose = AttachmentPurpose.MessageAttachment;
        _userRepo.GetAttachmentByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);

        var act = () => _sut.SetAvatarAsync(_user.UserId, attachment.AttachmentId, _user.Version);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SetAvatar_UserNotFound_Throws()
    {
        _userRepo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = () => _sut.SetAvatarAsync("missing", Guid.NewGuid(), [0, 0, 0, 1]);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-1.6 Remove Avatar

    [Fact]
    public async Task RemoveAvatar_ClearsAvatarId()
    {
        _user.AvatarAttachmentId = Guid.NewGuid();

        await _sut.RemoveAvatarAsync(_user.UserId, _user.Version);

        _user.AvatarAttachmentId.Should().BeNull();
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveAvatar_SetsVersion()
    {
        var version = new byte[] { 5, 6, 7, 8 };

        await _sut.RemoveAvatarAsync(_user.UserId, version);

        _user.Version.Should().BeEquivalentTo(version);
    }

    [Fact]
    public async Task RemoveAvatar_UserNotFound_Throws()
    {
        _userRepo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = () => _sut.RemoveAvatarAsync("missing", [0, 0, 0, 1]);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-1.7 Delete Account

    [Fact]
    public async Task DeleteAccount_ScrubsUserRecord()
    {
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(false);

        await _sut.DeleteAccountAsync(_user.UserId);

        _user.DisplayName.Should().Be("Deleted User");
        _user.Bio.Should().BeNull();
        _user.AvatarAttachmentId.Should().BeNull();
        _user.DeletedAt.Should().NotBeNull();
        _user.Username.Should().StartWith("deleted_");
        _user.NormalizedUsername.Should().StartWith("DELETED_");
    }

    [Fact]
    public async Task DeleteAccount_CallsBulkCleanup()
    {
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(false);

        await _sut.DeleteAccountAsync(_user.UserId);

        await _userRepo.Received(1).DeleteAllUserBlocksAsync(_user.UserId, Arg.Any<CancellationToken>());
        await _userRepo.Received(1).DeleteAllReactionsAsync(_user.UserId, Arg.Any<CancellationToken>());
        await _userRepo.Received(1).DeactivateAllMembershipsAsync(_user.UserId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _userRepo.Received(1).ScrubAuditLogIpAddressesAsync(_user.UserId, Arg.Any<CancellationToken>());
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAccount_AnonymizesIdentity()
    {
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(false);

        await _sut.DeleteAccountAsync(_user.UserId);

        await _identityService.Received(1).AnonymizeIdentityAsync(_user.UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAccount_WithAvatar_DeletesBlob()
    {
        var avatar = CreateAvatar(_user.UserId);
        _user.Avatar = avatar;
        _user.AvatarAttachmentId = avatar.AttachmentId;
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(false);

        await _sut.DeleteAccountAsync(_user.UserId);

        await _userRepo.Received(1).DeleteAttachmentAsync(avatar.AttachmentId, Arg.Any<CancellationToken>());
        await _blobStorage.Received(1).DeleteAsync(avatar.StoragePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAccount_NoAvatar_SkipsBlobDelete()
    {
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(false);

        await _sut.DeleteAccountAsync(_user.UserId);

        await _blobStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAccount_OwnsChannels_Throws()
    {
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => _sut.DeleteAccountAsync(_user.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task DeleteAccount_UserNotFound_Throws()
    {
        _userRepo.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = () => _sut.DeleteAccountAsync("missing");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteAccount_IdentityAnonymizeFails_DoesNotThrow()
    {
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(false);
        _identityService.AnonymizeIdentityAsync(_user.UserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Identity provider down"));

        var act = () => _sut.DeleteAccountAsync(_user.UserId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAccount_BlobDeleteFails_DoesNotThrow()
    {
        var avatar = CreateAvatar(_user.UserId);
        _user.Avatar = avatar;
        _user.AvatarAttachmentId = avatar.AttachmentId;
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(false);
        _blobStorage.DeleteAsync(avatar.StoragePath, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Blob storage down"));

        var act = () => _sut.DeleteAccountAsync(_user.UserId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAccount_SavesBeforeExternalSideEffects()
    {
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(false);

        await _sut.DeleteAccountAsync(_user.UserId);

        // SaveChanges must be called before identity anonymize
        Received.InOrder(() =>
        {
            _userRepo.SaveChangesAsync(Arg.Any<CancellationToken>());
            _identityService.AnonymizeIdentityAsync(_user.UserId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task DeleteAccount_SaveFails_DoesNotCallExternalServices()
    {
        _user.Avatar = CreateAvatar(_user.UserId);
        _user.AvatarAttachmentId = _user.Avatar.AttachmentId;
        _userRepo.OwnsAnyChannelsAsync(_user.UserId, Arg.Any<CancellationToken>()).Returns(false);
        _userRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB commit failed"));

        var act = () => _sut.DeleteAccountAsync(_user.UserId);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _identityService.DidNotReceive().AnonymizeIdentityAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _blobStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // Helper

    private static Attachment CreateAvatar(string userId) => new()
    {
        AttachmentId = Guid.NewGuid(),
        UploadedByUserId = userId,
        Purpose = AttachmentPurpose.UserAvatar,
        FileName = "avatar.png",
        StoragePath = $"avatars/{userId}/avatar.png",
        ContentType = "image/png",
        FileSizeBytes = 1024,
        UploadedAt = DateTimeOffset.UtcNow
    };
}
