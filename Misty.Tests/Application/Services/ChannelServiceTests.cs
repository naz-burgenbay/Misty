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

public class ChannelServiceTests
{
    private readonly IChannelRepository _channelRepo = Substitute.For<IChannelRepository>();
    private readonly IBlobStorageProvider _blobStorage = Substitute.For<IBlobStorageProvider>();
    private readonly IValidator<CreateChannelRequest> _createValidator = Substitute.For<IValidator<CreateChannelRequest>>();
    private readonly IValidator<UpdateChannelRequest> _updateValidator = Substitute.For<IValidator<UpdateChannelRequest>>();
    private readonly IValidator<TransferOwnershipRequest> _transferValidator = Substitute.For<IValidator<TransferOwnershipRequest>>();
    private readonly ChannelService _sut;

    private readonly User _owner;
    private readonly User _memberUser;
    private readonly Channel _channel;
    private readonly ChannelMember _ownerMember;
    private readonly ChannelMember _regularMember;
    private readonly ChannelRole _ownerRole;

    public ChannelServiceTests()
    {
        _sut = new ChannelService(
            _channelRepo, _blobStorage,
            _createValidator, _updateValidator, _transferValidator,
            Substitute.For<ILogger<ChannelService>>());

        // Validators pass by default
        _createValidator.ValidateAsync(Arg.Any<ValidationContext<CreateChannelRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _updateValidator.ValidateAsync(Arg.Any<ValidationContext<UpdateChannelRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _transferValidator.ValidateAsync(Arg.Any<ValidationContext<TransferOwnershipRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        _owner = TestData.User(displayName: "Owner");
        _memberUser = TestData.User(displayName: "Member");

        _channel = TestData.Channel(_owner.UserId);
        _channel.Owner = _owner;

        _ownerMember = TestData.Member(_channel, _owner);
        _regularMember = TestData.Member(_channel, _memberUser);

        _ownerRole = TestData.Role(_channel, "Owner", ChannelPermission.Administrator, 100, isSystem: true);
        TestData.AssignRole(_ownerMember, _ownerRole);

        // Default wiring
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _owner.UserId, Arg.Any<CancellationToken>())
            .Returns(_ownerMember);
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, _memberUser.UserId, Arg.Any<CancellationToken>())
            .Returns(_regularMember);
        _channelRepo.GetByIdAsync(_channel.ChannelId, Arg.Any<CancellationToken>())
            .Returns(_channel);
    }

    // UC-5.1 Create Channel

    [Fact]
    public async Task CreateChannel_Succeeds()
    {
        var request = new CreateChannelRequest { Name = "General", IsPrivate = false };

        _channelRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ch = TestData.Channel(_owner.UserId);
                ch.Owner = _owner;
                return ch;
            });
        _channelRepo.GetActiveMemberAsync(Arg.Any<Guid>(), _owner.UserId, Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var ch = TestData.Channel(_owner.UserId);
                ch.Owner = _owner;
                var m = TestData.Member(ch, _owner);
                var role = TestData.Role(ch, "Owner", ChannelPermission.Administrator, 100, isSystem: true);
                TestData.AssignRole(m, role);
                return m;
            });

        var result = await _sut.CreateChannelAsync(_owner.UserId, request);

        result.Name.Should().NotBeNullOrEmpty();
        await _channelRepo.Received(1).AddChannelAsync(
            Arg.Is<Channel>(c => c.OwnerUserId == _owner.UserId && c.MemberCount == 1),
            Arg.Any<CancellationToken>());
        // 2 system roles: Owner + Moderator
        await _channelRepo.Received(2).AddRoleAsync(Arg.Any<ChannelRole>(), Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).AddMemberAsync(Arg.Any<ChannelMember>(), Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).AddMemberRoleAsync(Arg.Any<ChannelMemberRole>(), Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // UC-5.4 Update Channel

    [Fact]
    public async Task UpdateChannel_WithEditPermission_Succeeds()
    {
        var editorUser = TestData.User(displayName: "Editor");
        var editorMember = TestData.Member(_channel, editorUser);
        var editorRole = TestData.Role(_channel, "Editor", ChannelPermission.EditChannel, 30);
        TestData.AssignRole(editorMember, editorRole);

        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, editorUser.UserId, Arg.Any<CancellationToken>())
            .Returns(editorMember);

        var request = new UpdateChannelRequest
        {
            Name = "Renamed",
            Description = "New desc",
            Version = _channel.Version
        };

        await _sut.UpdateChannelAsync(_channel.ChannelId, editorUser.UserId, request);

        _channel.Name.Should().Be("Renamed");
        _channel.Description.Should().Be("New desc");
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(log => log.Action == AuditAction.ChannelUpdated),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateChannel_WithoutEditPermission_Throws()
    {
        var request = new UpdateChannelRequest { Name = "Hacked", Version = _channel.Version };

        var act = () => _sut.UpdateChannelAsync(_channel.ChannelId, _memberUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // UC-5.5 Delete Channel

    [Fact]
    public async Task DeleteChannel_Owner_SetsDeletedAt()
    {
        await _sut.DeleteChannelAsync(_channel.ChannelId, _owner.UserId);

        _channel.DeletedAt.Should().NotBeNull();
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(log => log.Action == AuditAction.ChannelDeleted),
            Arg.Any<CancellationToken>());
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteChannel_NonOwner_Throws()
    {
        var act = () => _sut.DeleteChannelAsync(_channel.ChannelId, _memberUser.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // UC-5.6 Join Channel by Invite Code

    [Fact]
    public async Task JoinByInviteCode_Succeeds()
    {
        _channel.InviteCode = "ABC123";
        _channelRepo.GetByInviteCodeAsync("ABC123", Arg.Any<CancellationToken>())
            .Returns(_channel);
        // New user not an existing member
        var newUser = TestData.User(displayName: "Newbie");
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, newUser.UserId, Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);
        _channelRepo.HasActiveBanAsync(_channel.ChannelId, newUser.UserId, Arg.Any<CancellationToken>())
            .Returns(false);

        // After save, return the newly created member for response mapping
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, newUser.UserId, Arg.Any<CancellationToken>())
            .Returns(
                (ChannelMember?)null, // first call: not member yet
                TestData.Member(_channel, newUser) // second call: after save
            );

        var result = await _sut.JoinByInviteCodeAsync("ABC123", newUser.UserId);

        result.Should().NotBeNull();
        await _channelRepo.Received(1).AddMemberAsync(
            Arg.Is<ChannelMember>(m => m.UserId == newUser.UserId && m.ChannelId == _channel.ChannelId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinByInviteCode_AlreadyMember_Throws()
    {
        _channel.InviteCode = "ABC123";
        _channelRepo.GetByInviteCodeAsync("ABC123", Arg.Any<CancellationToken>())
            .Returns(_channel);
        // memberUser is already a member (wired in constructor)

        var act = () => _sut.JoinByInviteCodeAsync("ABC123", _memberUser.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task JoinByInviteCode_Banned_Throws()
    {
        _channel.InviteCode = "ABC123";
        _channelRepo.GetByInviteCodeAsync("ABC123", Arg.Any<CancellationToken>())
            .Returns(_channel);

        var bannedUser = TestData.User(displayName: "Banned");
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, bannedUser.UserId, Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);
        _channelRepo.HasActiveBanAsync(_channel.ChannelId, bannedUser.UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        var act = () => _sut.JoinByInviteCodeAsync("ABC123", bannedUser.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task JoinByInviteCode_InvalidCode_ThrowsNotFound()
    {
        _channelRepo.GetByInviteCodeAsync("INVALID", Arg.Any<CancellationToken>())
            .Returns((Channel?)null);

        var act = () => _sut.JoinByInviteCodeAsync("INVALID", _memberUser.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-5.7 Generate Invite Code

    [Fact]
    public async Task GenerateInviteCode_WithPermission_ReturnsCode()
    {
        var inviteUser = TestData.User(displayName: "Inviter");
        var inviteMember = TestData.Member(_channel, inviteUser);
        var inviteRole = TestData.Role(_channel, "InviteManager", ChannelPermission.ManageInvites, 30);
        TestData.AssignRole(inviteMember, inviteRole);

        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, inviteUser.UserId, Arg.Any<CancellationToken>())
            .Returns(inviteMember);

        var result = await _sut.GenerateInviteCodeAsync(_channel.ChannelId, inviteUser.UserId);

        result.Should().NotBeNullOrEmpty();
        _channel.InviteCode.Should().Be(result);
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(log => log.Action == AuditAction.InviteCreated),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateInviteCode_WithoutPermission_Throws()
    {
        var act = () => _sut.GenerateInviteCodeAsync(_channel.ChannelId, _memberUser.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GenerateInviteCode_CollisionRetry_SucceedsOnSecondAttempt()
    {
        // Owner has ManageInvites via Administrator
        var callCount = 0;
        _channelRepo.When(x => x.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ =>
            {
                callCount++;
                if (callCount == 1) // first audit log + save → collision
                    throw new DuplicateException("InviteCode collision");
            });

        var result = await _sut.GenerateInviteCodeAsync(_channel.ChannelId, _owner.UserId);

        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GenerateInviteCode_AllCollisions_ThrowsBusinessRule()
    {
        _channelRepo.When(x => x.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new DuplicateException("InviteCode collision"));

        var act = () => _sut.GenerateInviteCodeAsync(_channel.ChannelId, _owner.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // UC-5.8 Revoke Invite Code

    [Fact]
    public async Task RevokeInviteCode_ClearsCode()
    {
        _channel.InviteCode = "OLD_CODE";

        await _sut.RevokeInviteCodeAsync(_channel.ChannelId, _owner.UserId);

        _channel.InviteCode.Should().BeNull();
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(log => log.Action == AuditAction.InviteRevoked),
            Arg.Any<CancellationToken>());
    }

    // UC-5.9 Transfer Ownership

    [Fact]
    public async Task TransferOwnership_SwapsOwnerRole()
    {
        var ownerRoleAssignment = _ownerMember.AssignedRoles.First();

        _channelRepo.GetSystemRoleAsync(_channel.ChannelId, "Owner", Arg.Any<CancellationToken>())
            .Returns(_ownerRole);
        _channelRepo.GetMemberRoleAssignmentAsync(_ownerMember.ChannelMemberId, _ownerRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns(ownerRoleAssignment);
        _channelRepo.GetMemberRoleAssignmentAsync(_regularMember.ChannelMemberId, _ownerRole.ChannelRoleId, Arg.Any<CancellationToken>())
            .Returns((ChannelMemberRole?)null);

        var request = new TransferOwnershipRequest { NewOwnerUserId = _memberUser.UserId };

        await _sut.TransferOwnershipAsync(_channel.ChannelId, _owner.UserId, request);

        _channel.OwnerUserId.Should().Be(_memberUser.UserId);
        // Old owner role removed
        _channelRepo.Received(1).RemoveMemberRoleAsync(ownerRoleAssignment);
        // New owner role assigned
        await _channelRepo.Received(1).AddMemberRoleAsync(
            Arg.Is<ChannelMemberRole>(r => r.ChannelMemberId == _regularMember.ChannelMemberId && r.ChannelRoleId == _ownerRole.ChannelRoleId),
            Arg.Any<CancellationToken>());
        // Audit log
        await _channelRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(log => log.Action == AuditAction.OwnershipTransferred),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TransferOwnership_NonOwner_Throws()
    {
        var request = new TransferOwnershipRequest { NewOwnerUserId = _owner.UserId };

        var act = () => _sut.TransferOwnershipAsync(_channel.ChannelId, _memberUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task TransferOwnership_ToSelf_Throws()
    {
        var request = new TransferOwnershipRequest { NewOwnerUserId = _owner.UserId };

        var act = () => _sut.TransferOwnershipAsync(_channel.ChannelId, _owner.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task TransferOwnership_TargetNotMember_ThrowsNotFound()
    {
        _channelRepo.GetActiveMemberAsync(_channel.ChannelId, "unknown", Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        var request = new TransferOwnershipRequest { NewOwnerUserId = "unknown" };

        var act = () => _sut.TransferOwnershipAsync(_channel.ChannelId, _owner.UserId, request);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-5.10 Set Channel Icon

    [Fact]
    public async Task SetChannelIcon_ValidAttachment_Succeeds()
    {
        var attachmentId = Guid.NewGuid();
        var attachment = new Attachment
        {
            AttachmentId = attachmentId,
            UploadedByUserId = _owner.UserId,
            Purpose = AttachmentPurpose.ChannelIcon,
            FileName = "icon.png",
            StoragePath = "icons/icon.png",
            ContentType = "image/png"
        };

        _channelRepo.GetAttachmentByIdAsync(attachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);

        await _sut.SetChannelIconAsync(_channel.ChannelId, _owner.UserId, attachmentId);

        _channel.IconAttachmentId.Should().Be(attachmentId);
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetChannelIcon_WrongPurpose_Throws()
    {
        var attachment = new Attachment
        {
            AttachmentId = Guid.NewGuid(),
            UploadedByUserId = _owner.UserId,
            Purpose = AttachmentPurpose.MessageAttachment, // wrong
            FileName = "file.png",
            StoragePath = "path",
            ContentType = "image/png"
        };

        _channelRepo.GetAttachmentByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);

        var act = () => _sut.SetChannelIconAsync(_channel.ChannelId, _owner.UserId, attachment.AttachmentId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SetChannelIcon_NotOwnedByUser_Throws()
    {
        var attachment = new Attachment
        {
            AttachmentId = Guid.NewGuid(),
            UploadedByUserId = _memberUser.UserId, // not the owner
            Purpose = AttachmentPurpose.ChannelIcon,
            FileName = "icon.png",
            StoragePath = "path",
            ContentType = "image/png"
        };

        _channelRepo.GetAttachmentByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);

        var act = () => _sut.SetChannelIconAsync(_channel.ChannelId, _owner.UserId, attachment.AttachmentId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    // UC-5.11 Remove Channel Icon

    [Fact]
    public async Task RemoveChannelIcon_ClearsIcon()
    {
        _channel.IconAttachmentId = Guid.NewGuid();

        await _sut.RemoveChannelIconAsync(_channel.ChannelId, _owner.UserId);

        _channel.IconAttachmentId.Should().BeNull();
        await _channelRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
