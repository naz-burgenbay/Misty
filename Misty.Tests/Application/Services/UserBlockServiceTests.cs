using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Application.Services;
using Misty.Domain.Entities;
using Misty.Tests.Common;
using NSubstitute;

namespace Misty.Tests.Application.Services;

public class UserBlockServiceTests
{
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IBlobStorageProvider _blobStorage = Substitute.For<IBlobStorageProvider>();
    private readonly IValidator<CreateUserBlockRequest> _validator = Substitute.For<IValidator<CreateUserBlockRequest>>();
    private readonly UserBlockService _sut;

    private readonly User _userA;
    private readonly User _userB;

    public UserBlockServiceTests()
    {
        _sut = new UserBlockService(
            _userRepo, _blobStorage, _validator,
            Substitute.For<ILogger<UserBlockService>>());

        _validator.ValidateAsync(Arg.Any<ValidationContext<CreateUserBlockRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        _userA = TestData.User(displayName: "Naz");
        _userB = TestData.User(displayName: "Jonas");

        _userRepo.GetByIdAsync(_userB.UserId, Arg.Any<CancellationToken>())
            .Returns(_userB);
    }

    // UC-9.1 Block User

    [Fact]
    public async Task BlockUser_ValidRequest_CreatesBlock()
    {
        var request = new CreateUserBlockRequest { BlockedUserId = _userB.UserId };

        var result = await _sut.BlockUserAsync(_userA.UserId, request);

        result.BlockedUser.DisplayName.Should().Be("Jonas");
        result.BlockedUser.Id.Should().Be(_userB.UserId);
        await _userRepo.Received(1).AddBlockAsync(
            Arg.Is<UserBlock>(b => b.BlockingUserId == _userA.UserId && b.BlockedUserId == _userB.UserId),
            Arg.Any<CancellationToken>());
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BlockUser_CannotBlockSelf()
    {
        var request = new CreateUserBlockRequest { BlockedUserId = _userA.UserId };

        var act = () => _sut.BlockUserAsync(_userA.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task BlockUser_TargetNotFound_ThrowsNotFound()
    {
        _userRepo.GetByIdAsync("nonexistent", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var request = new CreateUserBlockRequest { BlockedUserId = "nonexistent" };

        var act = () => _sut.BlockUserAsync(_userA.UserId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-9.2 Unblock User

    [Fact]
    public async Task UnblockUser_OwnBlock_Removes()
    {
        var block = TestData.Block(_userA.UserId, _userB.UserId);
        _userRepo.GetBlockByIdAsync(block.UserBlockId, Arg.Any<CancellationToken>())
            .Returns(block);

        await _sut.UnblockUserAsync(_userA.UserId, block.UserBlockId);

        _userRepo.Received(1).RemoveBlock(block);
        await _userRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnblockUser_OtherUsersBlock_Throws()
    {
        var block = TestData.Block(_userB.UserId, _userA.UserId); // Jonas blocked Naz
        _userRepo.GetBlockByIdAsync(block.UserBlockId, Arg.Any<CancellationToken>())
            .Returns(block);

        // Naz tries to remove Jonas's block
        var act = () => _sut.UnblockUserAsync(_userA.UserId, block.UserBlockId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task UnblockUser_NotFound_Throws()
    {
        _userRepo.GetBlockByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((UserBlock?)null);

        var act = () => _sut.UnblockUserAsync(_userA.UserId, Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-9.4 Ensure Not Blocked

    [Fact]
    public async Task EnsureNotBlocked_NoBlock_DoesNotThrow()
    {
        _userRepo.ExistsBlockBetweenAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        var act = () => _sut.EnsureNotBlockedAsync(_userA.UserId, _userB.UserId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureNotBlocked_BlockExists_Throws()
    {
        _userRepo.ExistsBlockBetweenAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        var act = () => _sut.EnsureNotBlockedAsync(_userA.UserId, _userB.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
