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

public class ChannelRoleServiceTests
{
    private readonly IChannelRepository _channelRepo = Substitute.For<IChannelRepository>();
    private readonly IValidator<CreateChannelRoleRequest> _createValidator = Substitute.For<IValidator<CreateChannelRoleRequest>>();
    private readonly IValidator<UpdateChannelRoleRequest> _updateValidator = Substitute.For<IValidator<UpdateChannelRoleRequest>>();
    private readonly ChannelRoleService _sut;

    private readonly User _admin;
    private readonly User _regularUser;
    private readonly Channel _channel;
    private readonly ChannelMember _adminMember;
    private readonly ChannelMember _regularMember;
    private readonly ChannelRole _ownerRole;
    private readonly ChannelRole _customRole;

    public ChannelRoleServiceTests()
    {
        _sut = new ChannelRoleService(
            _channelRepo, _createValidator, _updateValidator,
            Substitute.For<ILogger<ChannelRoleService>>());

        // Validators pass by default
        _createValidator.ValidateAsync(Arg.Any<ValidationContext<CreateChannelRoleRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _updateValidator.ValidateAsync(Arg.Any<ValidationContext<UpdateChannelRoleRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        _admin = TestData.User(displayName: "Admin");
        _regularUser = TestData.User(displayName: "Regular");

        _channel = TestData.Channel(_admin.UserId);
        _channel.Owner = _admin;

        _adminMember = TestData.Member(_channel, _admin);
        _regularMember = TestData.Member(_channel, _regularUser);

        _ownerRole = TestData.Role(_channel, "Owner", ChannelPermission.Administrator, 100, isSystem: true);
        _customRole = TestData.Role(_channel, "CustomRole", ChannelPermission.SendMessages, 10);
        TestData.AssignRole(_adminMember, _ownerRole);

        // Default wiring
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _admin.UserId, Arg.Any<CancellationToken>())
            .Returns(_adminMember);
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _regularUser.UserId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);
        _channelRepo.GetRoleByIdAsync(_customRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(_customRole);
        _channelRepo.GetRoleByIdAsync(_ownerRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(_ownerRole);
    }

    // UC-7.1 List Channel Roles

    [Fact]
    public async Task GetRoles_ReturnsRolesWithCounts()
    {
        _channelRepo.GetChannelRolesAsync(_channel.ChannelId, Arg.Any<CancellationToken>())
            .Returns(new List<ChannelRole> { _ownerRole, _customRole } as IReadOnlyList<ChannelRole>);
        _channelRepo.GetAssignedMemberCountsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, int>
            {
                [_ownerRole.ChannelRoleId] = 1,
                [_customRole.ChannelRoleId] = 3
            });

        var result = await _sut.GetRolesAsync(_channel.ChannelId, _admin.UserId);

        result.Should().HaveCount(2);
        result.First(r => r.Name == "CustomRole").AssignedMemberCount.Should().Be(3);
    }

    [Fact]
    public async Task GetRoles_NotAMember_ThrowsNotFound()
    {
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, "stranger", Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        var act = () => _sut.GetRolesAsync(_channel.ChannelId, "stranger");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-7.2 Get Channel Role

    [Fact]
    public async Task GetRole_Succeeds()
    {
        _channelRepo.GetAssignedMemberCountAsync(_customRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(5);

        var result = await _sut.GetRoleAsync(
            _channel.ChannelId, _customRole.ChannelRoleId, _admin.UserId);

        result.ChannelRoleId.Should().Be(_customRole.ChannelRoleId);
        result.Name.Should().Be("CustomRole");
        result.AssignedMemberCount.Should().Be(5);
    }

    [Fact]
    public async Task GetRole_NotFound_ThrowsNotFound()
    {
        var missingId = Guid.NewGuid();
        _channelRepo.GetRoleByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((ChannelRole?)null);

        var act = () => _sut.GetRoleAsync(_channel.ChannelId, missingId, _admin.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetRole_WrongChannel_ThrowsNotFound()
    {
        var otherChannel = TestData.Channel(_admin.UserId);
        var foreignRole = TestData.Role(otherChannel, "Foreign", ChannelPermission.SendMessages, 10);
        _channelRepo.GetRoleByIdAsync(foreignRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(foreignRole);

        var act = () => _sut.GetRoleAsync(
            _channel.ChannelId, foreignRole.ChannelRoleId, _admin.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-7.3 Create Channel Role

    [Fact]
    public async Task CreateRole_Succeeds()
    {
        var request = new CreateChannelRoleRequest
        {
            Name = "Helper",
            Permissions = ChannelPermission.SendMessages,
            Position = 15
        };

        var result = await _sut.CreateRoleAsync(_channel.ChannelId, _admin.UserId, request);

        result.Name.Should().Be("Helper");
        result.Permissions.Should().Be(ChannelPermission.SendMessages);
        result.Position.Should().Be(15);
        result.IsSystemRole.Should().BeFalse();
        result.AssignedMemberCount.Should().Be(0);

        await _channelRepo.Received(1).AddRoleAsync(
            Arg.Is<ChannelRole>(r => r.Name == "Helper" && !r.IsSystemRole),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(l =>
                l.Action == AuditAction.RoleCreated &&
                l.ActorUserId == _admin.UserId &&
                l.ChannelId == _channel.ChannelId &&
                l.TargetType == "ChannelRole"),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateRole_NoPermission_ThrowsBusinessRule()
    {
        var request = new CreateChannelRoleRequest
        {
            Name = "Helper",
            Permissions = ChannelPermission.SendMessages,
            Position = 15
        };

        var act = () => _sut.CreateRoleAsync(_channel.ChannelId, _regularUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task CreateRole_NotAMember_ThrowsNotFound()
    {
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, "stranger", Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);
        var request = new CreateChannelRoleRequest
        {
            Name = "Helper",
            Permissions = ChannelPermission.SendMessages,
            Position = 15
        };

        var act = () => _sut.CreateRoleAsync(_channel.ChannelId, "stranger", request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-7.4 Update Channel Role

    [Fact]
    public async Task UpdateRole_Succeeds()
    {
        _channelRepo.GetAssignedMemberCountAsync(_customRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(2);

        var request = new UpdateChannelRoleRequest
        {
            Name = "Renamed",
            Permissions = ChannelPermission.KickUsers,
            Position = 25,
            Version = _customRole.Version
        };

        var result = await _sut.UpdateRoleAsync(
            _channel.ChannelId, _customRole.ChannelRoleId, _admin.UserId, request);

        result.Name.Should().Be("Renamed");
        result.Permissions.Should().Be(ChannelPermission.KickUsers);
        result.Position.Should().Be(25);
        result.AssignedMemberCount.Should().Be(2);
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(l =>
                l.Action == AuditAction.RoleUpdated &&
                l.ActorUserId == _admin.UserId &&
                l.ChannelId == _channel.ChannelId &&
                l.TargetType == "ChannelRole" &&
                l.TargetId == _customRole.ChannelRoleId.ToString()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateRole_NoPermission_ThrowsBusinessRule()
    {
        var request = new UpdateChannelRoleRequest
        {
            Name = "Renamed",
            Version = _customRole.Version
        };

        var act = () => _sut.UpdateRoleAsync(
            _channel.ChannelId, _customRole.ChannelRoleId, _regularUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task UpdateRole_NotFound_ThrowsNotFound()
    {
        var missingId = Guid.NewGuid();
        _channelRepo.GetRoleByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((ChannelRole?)null);
        var request = new UpdateChannelRoleRequest { Version = [0, 0, 0, 1] };

        var act = () => _sut.UpdateRoleAsync(
            _channel.ChannelId, missingId, _admin.UserId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateRole_SystemRole_CannotModifyNameOrPermissions()
    {
        var request = new UpdateChannelRoleRequest
        {
            Name = "NotAllowed",
            Permissions = ChannelPermission.SendMessages,
            Version = _ownerRole.Version
        };

        var act = () => _sut.UpdateRoleAsync(
            _channel.ChannelId, _ownerRole.ChannelRoleId, _admin.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task UpdateRole_SystemRole_CanModifyPosition()
    {
        _channelRepo.GetAssignedMemberCountAsync(_ownerRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(1);

        var request = new UpdateChannelRoleRequest
        {
            Position = 200,
            Version = _ownerRole.Version
        };

        var result = await _sut.UpdateRoleAsync(
            _channel.ChannelId, _ownerRole.ChannelRoleId, _admin.UserId, request);

        result.Position.Should().Be(200);
    }

    // UC-7.5 Delete Channel Role

    [Fact]
    public async Task DeleteRole_Succeeds()
    {
        await _sut.DeleteRoleAsync(
            _channel.ChannelId, _customRole.ChannelRoleId, _admin.UserId, _customRole.Version);

        _channelRepo.Received(1).RemoveRole(_customRole);
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(l =>
                l.Action == AuditAction.RoleDeleted &&
                l.ActorUserId == _admin.UserId &&
                l.ChannelId == _channel.ChannelId &&
                l.TargetType == "ChannelRole" &&
                l.TargetId == _customRole.ChannelRoleId.ToString()),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteRole_NoPermission_ThrowsBusinessRule()
    {
        var act = () => _sut.DeleteRoleAsync(
            _channel.ChannelId, _customRole.ChannelRoleId, _regularUser.UserId, _customRole.Version);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task DeleteRole_NotFound_ThrowsNotFound()
    {
        var missingId = Guid.NewGuid();
        _channelRepo.GetRoleByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((ChannelRole?)null);

        var act = () => _sut.DeleteRoleAsync(
            _channel.ChannelId, missingId, _admin.UserId, [0, 0, 0, 1]);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task DeleteRole_SystemRole_ThrowsBusinessRule()
    {
        var act = () => _sut.DeleteRoleAsync(
            _channel.ChannelId, _ownerRole.ChannelRoleId, _admin.UserId, _ownerRole.Version);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
