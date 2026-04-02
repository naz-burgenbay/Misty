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

public class ChannelMemberServiceTests
{
    private readonly IChannelRepository _channelRepo = Substitute.For<IChannelRepository>();
    private readonly IBlobStorageProvider _blobStorage = Substitute.For<IBlobStorageProvider>();
    private readonly IValidator<MarkChannelReadRequest> _markReadValidator = Substitute.For<IValidator<MarkChannelReadRequest>>();
    private readonly IValidator<UpdateChannelMemberRolesRequest> _updateRolesValidator = Substitute.For<IValidator<UpdateChannelMemberRolesRequest>>();
    private readonly ChannelMemberService _sut;

    private readonly User _admin;
    private readonly User _regularUser;
    private readonly Channel _channel;
    private readonly ChannelMember _adminMember;
    private readonly ChannelMember _regularMember;
    private readonly ChannelRole _ownerRole;
    private readonly ChannelRole _moderatorRole;

    public ChannelMemberServiceTests()
    {
        _sut = new ChannelMemberService(
            _channelRepo, _blobStorage,
            _markReadValidator, _updateRolesValidator,
            Substitute.For<ILogger<ChannelMemberService>>());

        // Validators pass by default
        _markReadValidator.ValidateAsync(Arg.Any<ValidationContext<MarkChannelReadRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _updateRolesValidator.ValidateAsync(Arg.Any<ValidationContext<UpdateChannelMemberRolesRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        _admin = TestData.User(displayName: "Admin");
        _regularUser = TestData.User(displayName: "Regular");

        _channel = TestData.Channel(_admin.UserId);
        _channel.Owner = _admin;

        _adminMember = TestData.Member(_channel, _admin);
        _regularMember = TestData.Member(_channel, _regularUser);

        _ownerRole = TestData.Role(_channel, "Owner", ChannelPermission.Administrator, 100, isSystem: true);
        _moderatorRole = TestData.Role(_channel, "Moderator",
            ChannelPermission.KickUsers | ChannelPermission.ManageRoles, 50, isSystem: true);
        TestData.AssignRole(_adminMember, _ownerRole);

        // Default wiring
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _admin.UserId, Arg.Any<CancellationToken>())
            .Returns(_adminMember);
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _regularUser.UserId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);
        _channelRepo.GetMemberByIdAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);
        _channelRepo.GetMemberByIdAsync(_adminMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_adminMember);
    }

    // UC-6.1 List Channel Members

    [Fact]
    public async Task GetMembers_ReturnsPaged()
    {
        _channelRepo.GetMembersPagedAsync(_channel.ChannelId, 1, 20, Arg.Any<CancellationToken>())
            .Returns((new List<ChannelMember> { _adminMember, _regularMember } as IReadOnlyList<ChannelMember>, 2));

        var result = await _sut.GetMembersAsync(_channel.ChannelId, _admin.UserId, 1, 20);

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMembers_NotAMember_ThrowsNotFound()
    {
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, "stranger", Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        var act = () => _sut.GetMembersAsync(_channel.ChannelId, "stranger", 1, 20);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-6.2 Get Channel Member

    [Fact]
    public async Task GetMember_Succeeds()
    {
        var result = await _sut.GetMemberAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _admin.UserId);

        result.ChannelMemberId.Should().Be(_regularMember.ChannelMemberId);
        result.User.DisplayName.Should().Be("Regular");
    }

    [Fact]
    public async Task GetMember_TargetNotFound_ThrowsNotFound()
    {
        var missingId = Guid.NewGuid();
        _channelRepo.GetMemberByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        var act = () => _sut.GetMemberAsync(_channel.ChannelId, missingId, _admin.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetMember_WrongChannel_ThrowsNotFound()
    {
        var otherChannel = TestData.Channel(_admin.UserId);
        var foreignMember = TestData.Member(otherChannel, _regularUser);
        _channelRepo.GetMemberByIdAsync(foreignMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(foreignMember);

        var act = () => _sut.GetMemberAsync(
            _channel.ChannelId, foreignMember.ChannelMemberId, _admin.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-6.3 Leave Channel

    [Fact]
    public async Task LeaveChannel_Succeeds()
    {
        await _sut.LeaveChannelAsync(_channel.ChannelId, _regularUser.UserId);

        _regularMember.LeftAt.Should().NotBeNull();
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveChannel_OwnerCannotLeave_ThrowsBusinessRule()
    {
        var act = () => _sut.LeaveChannelAsync(_channel.ChannelId, _admin.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task LeaveChannel_AlreadyLeft_Idempotent()
    {
        _regularMember.LeftAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        await _sut.LeaveChannelAsync(_channel.ChannelId, _regularUser.UserId);

        await _channelRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveChannel_NotAMember_ThrowsNotFound()
    {
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, "stranger", Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        var act = () => _sut.LeaveChannelAsync(_channel.ChannelId, "stranger");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-6.4 Remove Member (Kick)

    [Fact]
    public async Task RemoveMember_Succeeds()
    {
        await _sut.RemoveMemberAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _admin.UserId);

        _regularMember.LeftAt.Should().NotBeNull();
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(l =>
                l.Action == AuditAction.MemberKicked &&
                l.ActorUserId == _admin.UserId &&
                l.ChannelId == _channel.ChannelId &&
                l.TargetType == "ChannelMember" &&
                l.TargetId == _regularMember.ChannelMemberId.ToString()),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveMember_NoPermission_ThrowsBusinessRule()
    {
        var act = () => _sut.RemoveMemberAsync(
            _channel.ChannelId, _adminMember.ChannelMemberId, _regularUser.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task RemoveMember_TargetNotFound_ThrowsNotFound()
    {
        var missingId = Guid.NewGuid();
        _channelRepo.GetMemberByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        var act = () => _sut.RemoveMemberAsync(_channel.ChannelId, missingId, _admin.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RemoveMember_TargetWrongChannel_ThrowsNotFound()
    {
        var otherChannel = TestData.Channel(_admin.UserId);
        var foreignMember = TestData.Member(otherChannel, _regularUser);
        _channelRepo.GetMemberByIdAsync(foreignMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(foreignMember);

        var act = () => _sut.RemoveMemberAsync(
            _channel.ChannelId, foreignMember.ChannelMemberId, _admin.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task RemoveMember_CannotKickSelf_ThrowsBusinessRule()
    {
        var act = () => _sut.RemoveMemberAsync(
            _channel.ChannelId, _adminMember.ChannelMemberId, _admin.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task RemoveMember_TargetOutranks_ThrowsBusinessRule()
    {
        // Give regular user mod powers to kick, but target admin outranks
        TestData.AssignRole(_regularMember, _moderatorRole);

        var act = () => _sut.RemoveMemberAsync(
            _channel.ChannelId, _adminMember.ChannelMemberId, _regularUser.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task RemoveMember_AlreadyLeft_Idempotent()
    {
        _regularMember.LeftAt = DateTimeOffset.UtcNow.AddMinutes(-5);

        await _sut.RemoveMemberAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _admin.UserId);

        await _channelRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // UC-6.5 Mark Channel as Read

    [Fact]
    public async Task MarkChannelRead_Succeeds()
    {
        var readAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var request = new MarkChannelReadRequest { LastReadAt = readAt };

        await _sut.MarkChannelReadAsync(_channel.ChannelId, _regularUser.UserId, request);

        _regularMember.LastReadAt.Should().Be(readAt);
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkChannelRead_FutureTimeClamped()
    {
        var futureTime = DateTimeOffset.UtcNow.AddHours(1);
        var request = new MarkChannelReadRequest { LastReadAt = futureTime };

        await _sut.MarkChannelReadAsync(_channel.ChannelId, _regularUser.UserId, request);

        _regularMember.LastReadAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task MarkChannelRead_NotAMember_ThrowsNotFound()
    {
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, "stranger", Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);
        var request = new MarkChannelReadRequest { LastReadAt = DateTimeOffset.UtcNow };

        var act = () => _sut.MarkChannelReadAsync(_channel.ChannelId, "stranger", request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-6.6 Update Member Roles

    [Fact]
    public async Task UpdateMemberRoles_AddsAndRemoves()
    {
        var existingRole = TestData.Role(_channel, "OldRole", ChannelPermission.SendMessages, 10);
        var newRole = TestData.Role(_channel, "NewRole", ChannelPermission.AttachFiles, 20);
        TestData.AssignRole(_regularMember, existingRole);

        var currentAssignment = _regularMember.AssignedRoles.First();
        _channelRepo.GetMemberRolesAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(new List<ChannelMemberRole> { currentAssignment } as IReadOnlyList<ChannelMemberRole>);

        _channelRepo.GetChannelRolesByIdsAsync(_channel.ChannelId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ChannelRole> { newRole } as IReadOnlyList<ChannelRole>);

        // After update, re-fetch returns updated member
        _channelRepo.GetMemberByIdAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);

        var request = new UpdateChannelMemberRolesRequest { RoleIds = [newRole.ChannelRoleId] };

        var result = await _sut.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _admin.UserId, request);

        _channelRepo.Received(1).RemoveMemberRoleAsync(currentAssignment);
        await _channelRepo.Received(1).AddMemberRoleAsync(
            Arg.Is<ChannelMemberRole>(r => r.ChannelRoleId == newRole.ChannelRoleId),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateMemberRoles_NoPermission_ThrowsBusinessRule()
    {
        var request = new UpdateChannelMemberRolesRequest { RoleIds = [] };

        var act = () => _sut.UpdateMemberRolesAsync(
            _channel.ChannelId, _adminMember.ChannelMemberId, _regularUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task UpdateMemberRoles_TargetNotFound_ThrowsNotFound()
    {
        var missingId = Guid.NewGuid();
        _channelRepo.GetMemberByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);
        var request = new UpdateChannelMemberRolesRequest { RoleIds = [] };

        var act = () => _sut.UpdateMemberRolesAsync(
            _channel.ChannelId, missingId, _admin.UserId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateMemberRoles_TargetOutranks_ThrowsBusinessRule()
    {
        TestData.AssignRole(_regularMember, _moderatorRole);
        var request = new UpdateChannelMemberRolesRequest { RoleIds = [] };

        var act = () => _sut.UpdateMemberRolesAsync(
            _channel.ChannelId, _adminMember.ChannelMemberId, _regularUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task UpdateMemberRoles_RoleNotFound_ThrowsNotFound()
    {
        var missingRoleId = Guid.NewGuid();
        _channelRepo.GetChannelRolesByIdsAsync(_channel.ChannelId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ChannelRole>() as IReadOnlyList<ChannelRole>);
        var request = new UpdateChannelMemberRolesRequest { RoleIds = [missingRoleId] };

        var act = () => _sut.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _admin.UserId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateMemberRoles_SystemRole_ThrowsBusinessRule()
    {
        _channelRepo.GetChannelRolesByIdsAsync(_channel.ChannelId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ChannelRole> { _ownerRole } as IReadOnlyList<ChannelRole>);
        var request = new UpdateChannelMemberRolesRequest { RoleIds = [_ownerRole.ChannelRoleId] };

        var act = () => _sut.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _admin.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task UpdateMemberRoles_EmptyList_RemovesAllRoles()
    {
        var existingRole = TestData.Role(_channel, "CustomRole", ChannelPermission.SendMessages, 10);
        TestData.AssignRole(_regularMember, existingRole);

        var currentAssignment = _regularMember.AssignedRoles.First();
        _channelRepo.GetMemberRolesAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(new List<ChannelMemberRole> { currentAssignment } as IReadOnlyList<ChannelMemberRole>);

        _channelRepo.GetMemberByIdAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);

        var request = new UpdateChannelMemberRolesRequest { RoleIds = [] };

        await _sut.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _admin.UserId, request);

        _channelRepo.Received(1).RemoveMemberRoleAsync(currentAssignment);
        await _channelRepo.DidNotReceive().AddMemberRoleAsync(
            Arg.Any<ChannelMemberRole>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateMemberRoles_AuditLogPerChange()
    {
        var roleA = TestData.Role(_channel, "RoleA", ChannelPermission.SendMessages, 10);
        var roleB = TestData.Role(_channel, "RoleB", ChannelPermission.AttachFiles, 20);

        _channelRepo.GetMemberRolesAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(new List<ChannelMemberRole>() as IReadOnlyList<ChannelMemberRole>);

        _channelRepo.GetChannelRolesByIdsAsync(_channel.ChannelId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ChannelRole> { roleA, roleB } as IReadOnlyList<ChannelRole>);

        _channelRepo.GetMemberByIdAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);

        var request = new UpdateChannelMemberRolesRequest
        {
            RoleIds = [roleA.ChannelRoleId, roleB.ChannelRoleId]
        };

        await _sut.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _admin.UserId, request);

        // Two roles added = two MemberRoleAssigned audit logs
        await _channelRepo.Received(2).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(l =>
                l.Action == AuditAction.MemberRoleAssigned &&
                l.ActorUserId == _admin.UserId &&
                l.ChannelId == _channel.ChannelId &&
                l.TargetType == "ChannelMember" &&
                l.TargetId == _regularMember.ChannelMemberId.ToString()),
            Arg.Any<CancellationToken>());
    }
}
