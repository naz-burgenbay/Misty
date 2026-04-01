using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs.Channels;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Application.Services;
using Misty.Domain.Entities;
using Misty.Domain.Enums;
using Misty.Tests.Common;
using NSubstitute;

namespace Misty.Tests.Application.Services;

public class ModerationServiceTests
{
    private readonly IChannelRepository _channelRepo = Substitute.For<IChannelRepository>();
    private readonly IBlobStorageProvider _blobStorage = Substitute.For<IBlobStorageProvider>();
    private readonly IValidator<CreateModerationActionRequest> _createValidator = Substitute.For<IValidator<CreateModerationActionRequest>>();
    private readonly IValidator<RevokeModerationActionRequest> _revokeValidator = Substitute.For<IValidator<RevokeModerationActionRequest>>();
    private readonly ModerationService _sut;

    // Shared test data
    private readonly User _ownerUser;
    private readonly User _modUser;
    private readonly User _targetUser;
    private readonly Channel _channel;
    private readonly ChannelMember _ownerMember;
    private readonly ChannelMember _modMember;
    private readonly ChannelMember _targetMember;

    public ModerationServiceTests()
    {
        _sut = new ModerationService(
            _channelRepo, _blobStorage,
            _createValidator, _revokeValidator,
            Substitute.For<ILogger<ModerationService>>());

        // Validators pass by default
        _createValidator.ValidateAsync(Arg.Any<ValidationContext<CreateModerationActionRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _revokeValidator.ValidateAsync(Arg.Any<ValidationContext<RevokeModerationActionRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        // Setup: owner, moderator (with MuteUsers + BanUsers at position 50), target (no roles)
        _ownerUser = TestData.User(displayName: "Owner");
        _modUser = TestData.User(displayName: "Moderator");
        _targetUser = TestData.User(displayName: "Target");
        _channel = TestData.Channel(_ownerUser.UserId, ChannelPermission.SendMessages);

        _ownerMember = TestData.Member(_channel, _ownerUser);
        _modMember = TestData.Member(_channel, _modUser);
        _targetMember = TestData.Member(_channel, _targetUser);

        var modRole = TestData.Role(_channel, "Moderator",
            ChannelPermission.MuteUsers | ChannelPermission.BanUsers | ChannelPermission.ViewAuditLog, 50);
        TestData.AssignRole(_modMember, modRole);

        // Wire up default repo returns for mod + target
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _modUser.UserId, Arg.Any<CancellationToken>())
            .Returns(_modMember);
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _targetUser.UserId, Arg.Any<CancellationToken>())
            .Returns(_targetMember);
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _ownerUser.UserId, Arg.Any<CancellationToken>())
            .Returns(_ownerMember);
    }

    // Helpers

    private ModerationAction CreateAction(ModerationType type, bool isActive = true, DateTimeOffset? expiresAt = null)
    {
        var action = TestData.Moderation(_channel, _targetUser.UserId, _modUser.UserId, type, isActive, expiresAt);
        action.TargetUser = _targetUser;
        action.CreatedBy = _modUser;
        return action;
    }

    private void SetupCreateHappyPath(ModerationType type)
    {
        _channelRepo.GetActiveModerationActionAsync(_channel.ChannelId, _targetUser.UserId, type, Arg.Any<CancellationToken>())
            .Returns((ModerationAction?)null);

        _channelRepo.GetModerationActionByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => CreateAction(type));
    }

    // UC-8.1 Create Moderation Action

    [Theory]
    [InlineData(ModerationType.Mute, AuditAction.MemberMuted)]
    [InlineData(ModerationType.Ban, AuditAction.MemberBanned)]
    [InlineData(ModerationType.Warning, AuditAction.MemberWarned)]
    public async Task CreateModerationAction_ValidRequest_Succeeds(ModerationType type, AuditAction expectedAudit)
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = _targetUser.UserId,
            Type = type,
            Reason = "Breaking rules"
        };

        SetupCreateHappyPath(type);

        var result = await _sut.CreateModerationActionAsync(
            _channel.ChannelId, _modUser.UserId, request);

        result.Type.Should().Be(type);
        result.TargetUser.DisplayName.Should().Be("Target");
        await _channelRepo.Received(1).AddModerationActionAsync(
            Arg.Is<ModerationAction>(a => a.Type == type && a.TargetUserId == _targetUser.UserId),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(log => log.Action == expectedAudit),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateModerationAction_CannotModerateSelf()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = _modUser.UserId,
            Type = ModerationType.Mute,
            Reason = "Self-mute"
        };

        var act = () => _sut.CreateModerationActionAsync(
            _channel.ChannelId, _modUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task CreateModerationAction_CannotModerateOwner()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = _ownerUser.UserId,
            Type = ModerationType.Ban,
            Reason = "Trying to ban owner"
        };

        var act = () => _sut.CreateModerationActionAsync(
            _channel.ChannelId, _modUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task CreateModerationAction_WithoutPermission_Throws()
    {
        _modMember.AssignedRoles.Clear();

        var request = new CreateModerationActionRequest
        {
            TargetUserId = _targetUser.UserId,
            Type = ModerationType.Mute,
            Reason = "No permission"
        };

        var act = () => _sut.CreateModerationActionAsync(
            _channel.ChannelId, _modUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task CreateModerationAction_LowerRoleCannotModerateHigherRole()
    {
        var seniorRole = TestData.Role(_channel, "Senior", ChannelPermission.MuteUsers, 100);
        TestData.AssignRole(_targetMember, seniorRole);

        var request = new CreateModerationActionRequest
        {
            TargetUserId = _targetUser.UserId,
            Type = ModerationType.Mute,
            Reason = "Testing hierarchy"
        };

        _channelRepo.GetActiveModerationActionAsync(_channel.ChannelId, _targetUser.UserId, ModerationType.Mute, Arg.Any<CancellationToken>())
            .Returns((ModerationAction?)null);

        var act = () => _sut.CreateModerationActionAsync(
            _channel.ChannelId, _modUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task CreateModerationAction_DuplicateActiveAction_ThrowsDuplicate()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = _targetUser.UserId,
            Type = ModerationType.Mute,
            Reason = "Double mute"
        };

        _channelRepo.GetActiveModerationActionAsync(_channel.ChannelId, _targetUser.UserId, ModerationType.Mute, Arg.Any<CancellationToken>())
            .Returns(TestData.Moderation(_channel, _targetUser.UserId, _modUser.UserId, ModerationType.Mute));

        var act = () => _sut.CreateModerationActionAsync(
            _channel.ChannelId, _modUser.UserId, request);

        await act.Should().ThrowAsync<DuplicateException>();
    }

    [Fact]
    public async Task CreateModerationAction_Ban_SetsLeftAtOnTarget()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = _targetUser.UserId,
            Type = ModerationType.Ban,
            Reason = "Banned"
        };

        SetupCreateHappyPath(ModerationType.Ban);

        await _sut.CreateModerationActionAsync(_channel.ChannelId, _modUser.UserId, request);

        _targetMember.LeftAt.Should().NotBeNull("ban should remove target from channel");
    }

    [Fact]
    public async Task CreateModerationAction_Mute_DoesNotSetLeftAt()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = _targetUser.UserId,
            Type = ModerationType.Mute,
            Reason = "Muted"
        };

        SetupCreateHappyPath(ModerationType.Mute);

        await _sut.CreateModerationActionAsync(_channel.ChannelId, _modUser.UserId, request);

        _targetMember.LeftAt.Should().BeNull("mute should not remove from channel");
    }

    [Fact]
    public async Task CreateModerationAction_ActorNotMember_ThrowsNotFound()
    {
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, "unknown-user", Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        var request = new CreateModerationActionRequest
        {
            TargetUserId = _targetUser.UserId,
            Type = ModerationType.Mute,
            Reason = "Test"
        };

        var act = () => _sut.CreateModerationActionAsync(
            _channel.ChannelId, "unknown-user", request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task CreateModerationAction_TargetNotMember_ThrowsNotFound()
    {
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _targetUser.UserId, Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        var request = new CreateModerationActionRequest
        {
            TargetUserId = _targetUser.UserId,
            Type = ModerationType.Mute,
            Reason = "Test"
        };

        var act = () => _sut.CreateModerationActionAsync(
            _channel.ChannelId, _modUser.UserId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-8.2 Revoke Moderation Action

    [Fact]
    public async Task RevokeModerationAction_ActiveAction_Deactivates()
    {
        var action = CreateAction(ModerationType.Mute);

        _channelRepo.GetModerationActionByIdAsync(action.ModerationActionId, Arg.Any<CancellationToken>())
            .Returns(action);

        var request = new RevokeModerationActionRequest { Reason = "Pardoned" };

        await _sut.RevokeModerationActionAsync(
            _channel.ChannelId, action.ModerationActionId, _modUser.UserId, request);

        action.IsActive.Should().BeFalse();
        action.UpdatedByUserId.Should().Be(_modUser.UserId);
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(log => log.Action == AuditAction.MemberUnmuted),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeModerationAction_AlreadyRevoked_Throws()
    {
        var action = CreateAction(ModerationType.Mute, isActive: false);

        _channelRepo.GetModerationActionByIdAsync(action.ModerationActionId, Arg.Any<CancellationToken>())
            .Returns(action);

        var request = new RevokeModerationActionRequest { Reason = "Too late" };

        var act = () => _sut.RevokeModerationActionAsync(
            _channel.ChannelId, action.ModerationActionId, _modUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task RevokeModerationAction_ExpiredAction_Throws()
    {
        var expired = DateTimeOffset.UtcNow.AddHours(-1);
        var action = CreateAction(ModerationType.Mute, isActive: true, expiresAt: expired);

        _channelRepo.GetModerationActionByIdAsync(action.ModerationActionId, Arg.Any<CancellationToken>())
            .Returns(action);

        var request = new RevokeModerationActionRequest();

        var act = () => _sut.RevokeModerationActionAsync(
            _channel.ChannelId, action.ModerationActionId, _modUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task RevokeModerationAction_WrongChannel_ThrowsNotFound()
    {
        var action = CreateAction(ModerationType.Mute);
        action.ChannelId = Guid.NewGuid(); // Different channel

        _channelRepo.GetModerationActionByIdAsync(action.ModerationActionId, Arg.Any<CancellationToken>())
            .Returns(action);

        var request = new RevokeModerationActionRequest();

        var act = () => _sut.RevokeModerationActionAsync(
            _channel.ChannelId, action.ModerationActionId, _modUser.UserId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-8.3 / UC-8.4 / UC-8.5 Read operations require ViewAuditLog

    [Fact]
    public async Task GetModerationAction_WithoutViewAuditLog_Throws()
    {
        var act = () => _sut.GetModerationActionAsync(
            _channel.ChannelId, Guid.NewGuid(), _targetUser.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetAuditLog_WithPermission_Succeeds()
    {
        _channelRepo.GetAuditLogsPagedAsync(_channel.ChannelId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<ChannelAuditLog>() as IReadOnlyList<ChannelAuditLog>, 0));

        var result = await _sut.GetAuditLogAsync(
            _channel.ChannelId, _modUser.UserId, 1, 20);

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }
}
