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

/// <summary>
/// Cross-cutting invariant tests, multi-step scenarios, and concurrency simulations
/// that span multiple channel operations (ChannelService, ChannelMemberService, ChannelRoleService).
/// </summary>
public class ChannelInvariantsTests
{
    private readonly IChannelRepository _channelRepo = Substitute.For<IChannelRepository>();
    private readonly IBlobStorageProvider _blobStorage = Substitute.For<IBlobStorageProvider>();

    // ChannelService
    private readonly IValidator<CreateChannelRequest> _createValidator = Substitute.For<IValidator<CreateChannelRequest>>();
    private readonly IValidator<UpdateChannelRequest> _updateValidator = Substitute.For<IValidator<UpdateChannelRequest>>();
    private readonly IValidator<TransferOwnershipRequest> _transferValidator = Substitute.For<IValidator<TransferOwnershipRequest>>();
    private readonly ChannelService _channelSvc;

    // ChannelMemberService
    private readonly IValidator<MarkChannelReadRequest> _markReadValidator = Substitute.For<IValidator<MarkChannelReadRequest>>();
    private readonly IValidator<UpdateChannelMemberRolesRequest> _updateRolesValidator = Substitute.For<IValidator<UpdateChannelMemberRolesRequest>>();
    private readonly ChannelMemberService _memberSvc;

    // ChannelRoleService
    private readonly IValidator<CreateChannelRoleRequest> _createRoleValidator = Substitute.For<IValidator<CreateChannelRoleRequest>>();
    private readonly IValidator<UpdateChannelRoleRequest> _updateRoleValidator = Substitute.For<IValidator<UpdateChannelRoleRequest>>();
    private readonly ChannelRoleService _roleSvc;

    private readonly User _owner;
    private readonly User _memberUser;
    private readonly User _thirdUser;
    private readonly Channel _channel;
    private readonly ChannelMember _ownerMember;
    private readonly ChannelMember _regularMember;
    private readonly ChannelMember _thirdMember;
    private readonly ChannelRole _ownerRole;
    private readonly ChannelRole _moderatorRole;
    private readonly ChannelRole _customRole;

    public ChannelInvariantsTests()
    {
        _channelSvc = new ChannelService(
            _channelRepo, _blobStorage,
            _createValidator, _updateValidator, _transferValidator,
            Substitute.For<ILogger<ChannelService>>());

        _memberSvc = new ChannelMemberService(
            _channelRepo, _blobStorage,
            _markReadValidator, _updateRolesValidator,
            Substitute.For<ILogger<ChannelMemberService>>());

        _roleSvc = new ChannelRoleService(
            _channelRepo, _createRoleValidator, _updateRoleValidator,
            Substitute.For<ILogger<ChannelRoleService>>());

        // All validators pass
        _createValidator.ValidateAsync(Arg.Any<ValidationContext<CreateChannelRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _updateValidator.ValidateAsync(Arg.Any<ValidationContext<UpdateChannelRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _transferValidator.ValidateAsync(Arg.Any<ValidationContext<TransferOwnershipRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _markReadValidator.ValidateAsync(Arg.Any<ValidationContext<MarkChannelReadRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _updateRolesValidator.ValidateAsync(Arg.Any<ValidationContext<UpdateChannelMemberRolesRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _createRoleValidator.ValidateAsync(Arg.Any<ValidationContext<CreateChannelRoleRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _updateRoleValidator.ValidateAsync(Arg.Any<ValidationContext<UpdateChannelRoleRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        // Entities
        _owner = TestData.User(displayName: "Owner");
        _memberUser = TestData.User(displayName: "Member");
        _thirdUser = TestData.User(displayName: "Third");

        _channel = TestData.Channel(_owner.UserId);
        _channel.Owner = _owner;

        _ownerMember = TestData.Member(_channel, _owner);
        _regularMember = TestData.Member(_channel, _memberUser);
        _thirdMember = TestData.Member(_channel, _thirdUser);

        _ownerRole = TestData.Role(_channel, "Owner", ChannelPermission.Administrator, 100, isSystem: true);
        _moderatorRole = TestData.Role(_channel, "Moderator",
            ChannelPermission.KickUsers | ChannelPermission.ManageRoles, 50, isSystem: true);
        _customRole = TestData.Role(_channel, "Helper", ChannelPermission.SendMessages, 10);
        TestData.AssignRole(_ownerMember, _ownerRole);

        // Default wiring
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _owner.UserId, Arg.Any<CancellationToken>())
            .Returns(_ownerMember);
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _memberUser.UserId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _thirdUser.UserId, Arg.Any<CancellationToken>())
            .Returns(_thirdMember);
        _channelRepo.GetByIdAsync(_channel.ChannelId, Arg.Any<CancellationToken>())
            .Returns(_channel);
        _channelRepo.GetMemberByIdAsync(_ownerMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_ownerMember);
        _channelRepo.GetMemberByIdAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);
        _channelRepo.GetMemberByIdAsync(_thirdMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_thirdMember);
        _channelRepo.GetRoleByIdAsync(_customRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(_customRole);
        _channelRepo.GetRoleByIdAsync(_ownerRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(_ownerRole);
    }

    // Cross Invariant: Owner must always be a member

    [Fact]
    public async Task Owner_CannotLeaveChannel()
    {
        var act = () => _memberSvc.LeaveChannelAsync(_channel.ChannelId, _owner.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
        _ownerMember.LeftAt.Should().BeNull();
    }

    [Fact]
    public async Task Owner_CannotKickThemselves()
    {
        var act = () => _memberSvc.RemoveMemberAsync(
            _channel.ChannelId, _ownerMember.ChannelMemberId, _owner.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
        _ownerMember.LeftAt.Should().BeNull();
    }

    [Fact]
    public async Task Owner_CannotBeKickedByLowerRole()
    {
        TestData.AssignRole(_regularMember, _moderatorRole);

        var act = () => _memberSvc.RemoveMemberAsync(
            _channel.ChannelId, _ownerMember.ChannelMemberId, _memberUser.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
        _ownerMember.LeftAt.Should().BeNull();
    }

    // Cross Invariant: TransferOwnership then leave, previous owner can now leave

    [Fact]
    public async Task TransferOwnership_ThenOldOwnerCanLeave()
    {
        // Setup transfer
        _channelRepo.GetSystemRoleAsync(_channel.ChannelId, "Owner", Arg.Any<CancellationToken>())
            .Returns(_ownerRole);
        _channelRepo.GetMemberRoleAssignmentAsync(_ownerMember.ChannelMemberId, _ownerRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(_ownerMember.AssignedRoles.First());
        _channelRepo.GetMemberRoleAssignmentAsync(_regularMember.ChannelMemberId, _ownerRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns((ChannelMemberRole?)null);

        var request = new TransferOwnershipRequest { NewOwnerUserId = _memberUser.UserId };
        await _channelSvc.TransferOwnershipAsync(_channel.ChannelId, _owner.UserId, request);

        // After transfer, channel.OwnerUserId changed
        _channel.OwnerUserId.Should().Be(_memberUser.UserId);

        // Old owner can now leave (no longer the owner)
        await _memberSvc.LeaveChannelAsync(_channel.ChannelId, _owner.UserId);
        _ownerMember.LeftAt.Should().NotBeNull();
    }

    // Cross Invariant: Member roles must belong to the same channel

    [Fact]
    public async Task UpdateMemberRoles_CrossChannelRole_ThrowsNotFound()
    {
        var otherChannel = TestData.Channel(_owner.UserId);
        var foreignRole = TestData.Role(otherChannel, "ForeignRole", ChannelPermission.SendMessages, 10);

        // Repo returns empty list because foreign role doesn't belong to this channel
        _channelRepo.GetChannelRolesByIdsAsync(_channel.ChannelId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ChannelRole>() as IReadOnlyList<ChannelRole>);

        var request = new UpdateChannelMemberRolesRequest { RoleIds = [foreignRole.ChannelRoleId] };

        var act = () => _memberSvc.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _owner.UserId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // Cross Invariant: System roles cannot be deleted, even by owner

    [Fact]
    public async Task SystemRole_CannotBeDeleted_ByOwner()
    {
        var act = () => _roleSvc.DeleteRoleAsync(
            _channel.ChannelId, _ownerRole.ChannelRoleId, _owner.UserId, _ownerRole.Version);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // Cross Invariant: System roles cannot be manually assigned

    [Fact]
    public async Task SystemRole_CannotBeManuallyAssigned()
    {
        _channelRepo.GetChannelRolesByIdsAsync(_channel.ChannelId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ChannelRole> { _ownerRole } as IReadOnlyList<ChannelRole>);

        var request = new UpdateChannelMemberRolesRequest { RoleIds = [_ownerRole.ChannelRoleId] };

        var act = () => _memberSvc.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _owner.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // Multi Step: Assign role, remove it, reassign back to original state

    [Fact]
    public async Task UpdateRoles_RemoveThenReassign_BackToOriginalState()
    {
        // Step 1: Assign custom role
        TestData.AssignRole(_regularMember, _customRole);
        var originalAssignment = _regularMember.AssignedRoles.Single();

        _channelRepo.GetMemberRolesAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(
                new List<ChannelMemberRole> { originalAssignment } as IReadOnlyList<ChannelMemberRole>,   // for remove call
                new List<ChannelMemberRole>() as IReadOnlyList<ChannelMemberRole>                          // for reassign call (after removal)
            );

        _channelRepo.GetChannelRolesByIdsAsync(_channel.ChannelId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ChannelRole> { _customRole } as IReadOnlyList<ChannelRole>);

        _channelRepo.GetMemberByIdAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);

        // Step 2: Remove all roles
        var removeRequest = new UpdateChannelMemberRolesRequest { RoleIds = [] };
        await _memberSvc.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _owner.UserId, removeRequest);

        _channelRepo.Received(1).RemoveMemberRoleAsync(originalAssignment);

        // Step 3: Reassign same role
        var reassignRequest = new UpdateChannelMemberRolesRequest { RoleIds = [_customRole.ChannelRoleId] };
        await _memberSvc.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _owner.UserId, reassignRequest);

        await _channelRepo.Received(1).AddMemberRoleAsync(
            Arg.Is<ChannelMemberRole>(r => r.ChannelRoleId == _customRole.ChannelRoleId),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // Multi Step: Kick member, then they rejoin, state is clean

    [Fact]
    public async Task KickMember_ThenRejoin_StateIsClean()
    {
        // Step 1: Kick
        await _memberSvc.RemoveMemberAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _owner.UserId);

        _regularMember.LeftAt.Should().NotBeNull();

        // Step 2: After kick, rejoin via invite code
        _channel.InviteCode = "REJOIN";
        _channelRepo.GetByInviteCodeAsync("REJOIN", Arg.Any<CancellationToken>())
            .Returns(_channel);

        // After kick, GetActiveMember returns null (no longer active)
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _memberUser.UserId, Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);
        _channelRepo.HasActiveBanAsync(_channel.ChannelId, _memberUser.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        // After rejoin saved, new member returned
        var rejoined = TestData.Member(_channel, _memberUser);
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _memberUser.UserId, Arg.Any<CancellationToken>())
            .Returns(
                (ChannelMember?)null,  // first: still not active
                rejoined               // second: after save
            );

        var result = await _channelSvc.JoinByInviteCodeAsync("REJOIN", _memberUser.UserId);

        result.Should().NotBeNull();
        rejoined.LeftAt.Should().BeNull("rejoined member should have clean state");
        await _channelRepo.Received(1).AddMemberAsync(
            Arg.Is<ChannelMember>(m => m.UserId == _memberUser.UserId && m.LeftAt == null),
            Arg.Any<CancellationToken>());
    }

    // Multi Step: TransferOwnership then role updates require new owner permission

    [Fact]
    public async Task TransferOwnership_OldOwnerLosesManageRolesPermission()
    {
        // Setup transfer
        _channelRepo.GetSystemRoleAsync(_channel.ChannelId, "Owner", Arg.Any<CancellationToken>())
            .Returns(_ownerRole);
        var ownerAssignment = _ownerMember.AssignedRoles.First();
        _channelRepo.GetMemberRoleAssignmentAsync(_ownerMember.ChannelMemberId, _ownerRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(ownerAssignment);
        _channelRepo.GetMemberRoleAssignmentAsync(_regularMember.ChannelMemberId, _ownerRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns((ChannelMemberRole?)null);

        await _channelSvc.TransferOwnershipAsync(
            _channel.ChannelId, _owner.UserId,
            new TransferOwnershipRequest { NewOwnerUserId = _memberUser.UserId });

        // After transfer, simulate that old owner no longer has Owner role
        _ownerMember.AssignedRoles.Clear();

        // Old owner should fail ManageRoles permission check for role creation
        var act = () => _roleSvc.CreateRoleAsync(
            _channel.ChannelId, _owner.UserId,
            new CreateChannelRoleRequest { Name = "NewRole", Permissions = ChannelPermission.SendMessages, Position = 5 });

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // Concurrency: Member leaves while being kicked (already-left idempotency)

    [Fact]
    public async Task LeaveWhileBeingKicked_AlreadyLeftIsIdempotent()
    {
        // Simulate: member left between permission check and save
        _regularMember.LeftAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        // Kick should see LeftAt already set and short-circuit
        await _memberSvc.RemoveMemberAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _owner.UserId);

        await _channelRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // Concurrency: Role deleted during assignment attempt

    [Fact]
    public async Task RoleDeletedDuringAssignment_ThrowsNotFound()
    {
        var ephemeralRole = TestData.Role(_channel, "Ephemeral", ChannelPermission.SendMessages, 15);

        // Role exists in ID lookup but returns empty (deleted between calls)
        _channelRepo.GetChannelRolesByIdsAsync(_channel.ChannelId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<ChannelRole>() as IReadOnlyList<ChannelRole>);

        var request = new UpdateChannelMemberRolesRequest { RoleIds = [ephemeralRole.ChannelRoleId] };

        var act = () => _memberSvc.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _owner.UserId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // Concurrency: Two admins update roles simultaneously (second sees stale state)

    [Fact]
    public async Task ConcurrentRoleUpdate_SecondCallStillProcesses()
    {
        var roleA = TestData.Role(_channel, "RoleA", ChannelPermission.SendMessages, 10);
        var roleB = TestData.Role(_channel, "RoleB", ChannelPermission.AttachFiles, 20);

        // First admin assigns roleA
        _channelRepo.GetMemberRolesAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(
                new List<ChannelMemberRole>() as IReadOnlyList<ChannelMemberRole>,                        // first call: empty
                new List<ChannelMemberRole>() as IReadOnlyList<ChannelMemberRole>                          // second call: still empty (stale)
            );

        _channelRepo.GetChannelRolesByIdsAsync(_channel.ChannelId, Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(
                new List<ChannelRole> { roleA } as IReadOnlyList<ChannelRole>,
                new List<ChannelRole> { roleB } as IReadOnlyList<ChannelRole>
            );

        _channelRepo.GetMemberByIdAsync(_regularMember.ChannelMemberId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);

        // First update: assign roleA
        await _memberSvc.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _owner.UserId,
            new UpdateChannelMemberRolesRequest { RoleIds = [roleA.ChannelRoleId] });

        // Second update: assign roleB (concurrent admin with stale view)
        await _memberSvc.UpdateMemberRolesAsync(
            _channel.ChannelId, _regularMember.ChannelMemberId, _owner.UserId,
            new UpdateChannelMemberRolesRequest { RoleIds = [roleB.ChannelRoleId] });

        // Both adds should have been called
        await _channelRepo.Received(1).AddMemberRoleAsync(
            Arg.Is<ChannelMemberRole>(r => r.ChannelRoleId == roleA.ChannelRoleId),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).AddMemberRoleAsync(
            Arg.Is<ChannelMemberRole>(r => r.ChannelRoleId == roleB.ChannelRoleId),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // Cross Invariant: Non-member gets NotFoundException for all operations

    [Fact]
    public async Task NonMember_CannotPerformAnyChannelOperation()
    {
        var stranger = "stranger-id";
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, stranger, Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        // ChannelMemberService: GetMembers
        var act1 = () => _memberSvc.GetMembersAsync(_channel.ChannelId, stranger, 1, 20);
        await act1.Should().ThrowAsync<NotFoundException>();

        // ChannelMemberService: LeaveChannel
        var act2 = () => _memberSvc.LeaveChannelAsync(_channel.ChannelId, stranger);
        await act2.Should().ThrowAsync<NotFoundException>();

        // ChannelRoleService: GetRoles
        var act3 = () => _roleSvc.GetRolesAsync(_channel.ChannelId, stranger);
        await act3.Should().ThrowAsync<NotFoundException>();

        // ChannelRoleService: CreateRole
        var act4 = () => _roleSvc.CreateRoleAsync(_channel.ChannelId, stranger,
            new CreateChannelRoleRequest { Name = "X", Permissions = ChannelPermission.SendMessages, Position = 1 });
        await act4.Should().ThrowAsync<NotFoundException>();

        // ChannelService: UpdateChannel
        var act5 = () => _channelSvc.UpdateChannelAsync(_channel.ChannelId, stranger,
            new UpdateChannelRequest { Name = "Hacked", Version = [0, 0, 0, 1] });
        await act5.Should().ThrowAsync<NotFoundException>();
    }
}
