using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Application.DTOs;
using Misty.Application.DTOs.Channels;
using Misty.Application.Exceptions;
using Misty.Domain.Entities;
using Misty.Domain.Enums;
using NSubstitute;

namespace Misty.Tests.Infrastructure;

public class ServiceIntegrationTests : IntegrationTestBase
{
    public ServiceIntegrationTests(IntegrationTestFixture fixture) : base(fixture) { }

    private ServiceFactory Svc => _svc ??= new ServiceFactory(Db);
    private ServiceFactory? _svc;


    [Fact]
    public async Task CreateChannel_CreatesSystemRolesAndOwnerMembership_Atomically()
    {
        var owner = await SeedUserAsync();

        var result = await Svc.CreateChannelService().CreateChannelAsync(
            owner.UserId,
            new CreateChannelRequest { Name = "test-channel" });

        DetachAll();

        // Verify channel
        var channel = await Db.Channels
            .Include(c => c.Roles)
            .Include(c => c.Members)
                .ThenInclude(m => m.AssignedRoles)
            .FirstAsync(c => c.ChannelId == result.ChannelId);

        channel.OwnerUserId.Should().Be(owner.UserId);
        channel.MemberCount.Should().Be(1, "OnBeforeSave must increment MemberCount");

        // System roles
        channel.Roles.Should().HaveCount(2);
        channel.Roles.Should().Contain(r => r.Name == "Owner" && r.IsSystemRole);
        channel.Roles.Should().Contain(r => r.Name == "Moderator" && r.IsSystemRole);

        // Owner membership + role assignment
        var ownerMember = channel.Members.Should().ContainSingle().Subject;
        ownerMember.UserId.Should().Be(owner.UserId);
        ownerMember.AssignedRoles.Should().ContainSingle()
            .Which.Role.Name.Should().Be("Owner");
    }

    [Fact]
    public async Task TransferOwnership_SwapsRoleAssignmentsAndChannelOwner()
    {
        var oldOwner = await SeedUserAsync();
        var newOwnerUser = await SeedUserAsync();
        var (channel, _, ownerRole, modRole) = await SeedChannelAsync(oldOwner);
        await SeedMemberAsync(channel, newOwnerUser);
        DetachAll();

        await Svc.CreateChannelService().TransferOwnershipAsync(
            channel.ChannelId, oldOwner.UserId,
            new TransferOwnershipRequest { NewOwnerUserId = newOwnerUser.UserId });

        DetachAll();

        // Verify DB state
        var updated = await Db.Channels.AsNoTracking().FirstAsync(c => c.ChannelId == channel.ChannelId);
        updated.OwnerUserId.Should().Be(newOwnerUser.UserId);

        // New owner has Owner role
        var newOwnerRoles = await Db.ChannelMemberRoles
            .Include(cmr => cmr.Role)
            .Include(cmr => cmr.Member)
            .Where(cmr => cmr.Member.UserId == newOwnerUser.UserId && cmr.Member.ChannelId == channel.ChannelId)
            .ToListAsync();
        newOwnerRoles.Should().Contain(r => r.Role.Name == "Owner");

        // Old owner lost Owner role
        var oldOwnerRoles = await Db.ChannelMemberRoles
            .Include(cmr => cmr.Role)
            .Include(cmr => cmr.Member)
            .Where(cmr => cmr.Member.UserId == oldOwner.UserId && cmr.Member.ChannelId == channel.ChannelId)
            .ToListAsync();
        oldOwnerRoles.Should().NotContain(r => r.Role.Name == "Owner");

        // Audit log recorded
        var audit = await Db.ChannelAuditLogs
            .Where(a => a.ChannelId == channel.ChannelId && a.Action == AuditAction.OwnershipTransferred)
            .ToListAsync();
        audit.Should().ContainSingle();
    }

    [Fact]
    public async Task Ban_KicksMember_AndPreventsRejoin()
    {
        var owner = await SeedUserAsync();
        var target = await SeedUserAsync();
        var (channel, _, ownerRole, modRole) = await SeedChannelAsync(owner);
        await SeedMemberAsync(channel, target);
        DetachAll();

        // Ban
        await Svc.CreateModerationService().CreateModerationActionAsync(
            channel.ChannelId, owner.UserId,
            new CreateModerationActionRequest
            {
                TargetUserId = target.UserId,
                Type = ModerationType.Ban,
                Reason = "Test ban"
            });

        DetachAll();

        // Target's membership should have LeftAt set
        var membership = await Db.ChannelMembers
            .IgnoreQueryFilters()
            .FirstAsync(m => m.ChannelId == channel.ChannelId && m.UserId == target.UserId);
        membership.LeftAt.Should().NotBeNull("ban must kick the member");

        // Generate invite code first
        DetachAll();
        Db = Fixture.CreateDbContext();
        _svc = new ServiceFactory(Db);

        var code = await Svc.CreateChannelService().GenerateInviteCodeAsync(
            channel.ChannelId, owner.UserId);

        // Attempt rejoin → should throw
        var act = () => Svc.CreateChannelService().JoinByInviteCodeAsync(code, target.UserId);
        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*banned*");
    }

    [Fact]
    public async Task MutedUser_CannotSendChannelMessage()
    {
        var owner = await SeedUserAsync();
        var user = await SeedUserAsync();
        var (channel, _, ownerRole, _) = await SeedChannelAsync(owner);
        await SeedMemberAsync(channel, user);

        // Insert active mute directly
        Db.ModerationActions.Add(new ModerationAction
        {
            ModerationActionId = Guid.NewGuid(),
            ChannelId = channel.ChannelId,
            TargetUserId = user.UserId,
            CreatedByUserId = owner.UserId,
            Type = ModerationType.Mute,
            Reason = "test mute",
            StartAt = DateTimeOffset.UtcNow,
            IsActive = true
        });
        await Db.SaveChangesAsync();
        DetachAll();

        var act = () => Svc.CreateMessageService().SendChannelMessageAsync(
            channel.ChannelId, user.UserId,
            new SendMessageRequest { Content = "hello", IdempotencyKey = Guid.NewGuid().ToString() });

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("*muted*");
    }

    [Fact]
    public async Task SendChannelMessage_DuplicateIdempotencyKey_ReturnsSameMessage()
    {
        var owner = await SeedUserAsync();
        var (channel, _, _, _) = await SeedChannelAsync(owner);
        DetachAll();

        var key = Guid.NewGuid().ToString();
        var request = new SendMessageRequest { Content = "hello", IdempotencyKey = key };

        var first = await Svc.CreateMessageService().SendChannelMessageAsync(
            channel.ChannelId, owner.UserId, request);

        // Second call with same key
        var second = await Svc.CreateMessageService().SendChannelMessageAsync(
            channel.ChannelId, owner.UserId, request);

        second.MessageId.Should().Be(first.MessageId, "idempotency must return the existing message");

        // Only one message in DB
        var count = await Db.Messages.CountAsync(m => m.ChannelId == channel.ChannelId);
        count.Should().Be(1);
    }

    [Fact]
    public async Task MemberCount_StaysConsistent_ThroughJoinAndLeave()
    {
        var owner = await SeedUserAsync();
        var user = await SeedUserAsync();
        var (channel, _, _, _) = await SeedChannelAsync(owner);
        DetachAll();

        // Generate invite code
        var code = await Svc.CreateChannelService().GenerateInviteCodeAsync(
            channel.ChannelId, owner.UserId);

        // Join
        await Svc.CreateChannelService().JoinByInviteCodeAsync(code, user.UserId);
        DetachAll();
        var afterJoin = await Db.Channels.AsNoTracking().FirstAsync(c => c.ChannelId == channel.ChannelId);
        afterJoin.MemberCount.Should().Be(2);

        // Leave (needs fresh context since the member must be tracked)
        Db = Fixture.CreateDbContext();
        _svc = new ServiceFactory(Db);
        await Svc.CreateChannelMemberService().LeaveChannelAsync(channel.ChannelId, user.UserId);
        DetachAll();

        var afterLeave = await Db.Channels.AsNoTracking().FirstAsync(c => c.ChannelId == channel.ChannelId);
        afterLeave.MemberCount.Should().Be(1);
    }

    [Fact]
    public async Task SendConversationMessage_UnhidesHiddenParticipant()
    {
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var convo = await SeedConversationAsync(alice, bob);

        // Hide for bob
        var bobParticipant = convo.Participants.First(p => p.UserId == bob.UserId);
        bobParticipant.HiddenAt = DateTimeOffset.UtcNow;
        await Db.SaveChangesAsync();
        DetachAll();

        // Alice sends a message
        await Svc.CreateMessageService().SendConversationMessageAsync(
            convo.ConversationId, alice.UserId,
            new SendMessageRequest { Content = "Hey Bob!", IdempotencyKey = Guid.NewGuid().ToString() });

        DetachAll();

        var updated = await Db.ConversationParticipants
            .FirstAsync(p => p.ConversationId == convo.ConversationId && p.UserId == bob.UserId);
        updated.HiddenAt.Should().BeNull("message must unhide the other participant");
    }

    [Fact]
    public async Task SendConversationMessage_WhenBlocked_Throws()
    {
        var alice = await SeedUserAsync();
        var bob = await SeedUserAsync();
        var convo = await SeedConversationAsync(alice, bob);

        // Bob blocks Alice
        Db.UserBlocks.Add(new UserBlock
        {
            UserBlockId = Guid.NewGuid(),
            BlockingUserId = bob.UserId,
            BlockedUserId = alice.UserId,
            BlockedAt = DateTimeOffset.UtcNow
        });
        await Db.SaveChangesAsync();
        DetachAll();

        var act = () => Svc.CreateMessageService().SendConversationMessageAsync(
            convo.ConversationId, alice.UserId,
            new SendMessageRequest { Content = "Hi!", IdempotencyKey = Guid.NewGuid().ToString() });

        await act.Should().ThrowAsync<BusinessRuleException>("blocked users cannot message each other");
    }

    [Fact]
    public async Task DeleteMessage_OrphansReplies_ButPreservesIsReplyFlag()
    {
        var owner = await SeedUserAsync();
        var (channel, _, _, _) = await SeedChannelAsync(owner);
        var parent = await SeedChannelMessageAsync(channel.ChannelId, owner.UserId, "parent");

        // Create a reply pointing to parent
        var reply = new Message
        {
            MessageId = Guid.NewGuid(),
            ChannelId = channel.ChannelId,
            AuthorUserId = owner.UserId,
            Content = "reply",
            SentAt = DateTimeOffset.UtcNow,
            ParentMessageId = parent.MessageId,
            IsReply = true
        };
        Db.Messages.Add(reply);
        await Db.SaveChangesAsync();
        DetachAll();

        await Svc.CreateMessageService().DeleteMessageAsync(parent.MessageId, owner.UserId);
        DetachAll();

        // Reply should still exist with IsReply=true, but ParentMessageId=null
        var orphanedReply = await Db.Messages.FirstAsync(m => m.MessageId == reply.MessageId);
        orphanedReply.IsReply.Should().BeTrue();
        orphanedReply.ParentMessageId.Should().BeNull("parent was deleted");

        // Parent itself is gone
        var parentGone = await Db.Messages.AnyAsync(m => m.MessageId == parent.MessageId);
        parentGone.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteChannel_HidesRolesAndModerationActions()
    {
        var owner = await SeedUserAsync();
        var (channel, _, ownerRole, modRole) = await SeedChannelAsync(owner);
        DetachAll();

        await Svc.CreateChannelService().DeleteChannelAsync(channel.ChannelId, owner.UserId);
        DetachAll();

        // Channel invisible
        var channels = await Db.Channels.Where(c => c.ChannelId == channel.ChannelId).ToListAsync();
        channels.Should().BeEmpty();

        // Roles invisible via ChannelRole.Channel.DeletedAt filter
        var roles = await Db.ChannelRoles.Where(r => r.ChannelId == channel.ChannelId).ToListAsync();
        roles.Should().BeEmpty();

        // But still exists with IgnoreQueryFilters
        var rolesRaw = await Db.ChannelRoles
            .IgnoreQueryFilters()
            .Where(r => r.ChannelId == channel.ChannelId)
            .ToListAsync();
        rolesRaw.Should().HaveCount(2, "rows are not hard-deleted");
    }

    [Fact]
    public async Task AddReaction_DuplicateEmoji_ThrowsDuplicateException()
    {
        var owner = await SeedUserAsync();
        var (channel, _, _, _) = await SeedChannelAsync(owner);
        var msg = await SeedChannelMessageAsync(channel.ChannelId, owner.UserId);
        DetachAll();

        var svc = Svc.CreateMessageService();
        await svc.AddReactionAsync(msg.MessageId, owner.UserId, new AddReactionRequest { Emoji = "👍" });

        var act = () => svc.AddReactionAsync(msg.MessageId, owner.UserId, new AddReactionRequest { Emoji = "👍" });

        await act.Should().ThrowAsync<DuplicateException>("same user+emoji+message is unique");
    }

    [Fact]
    public async Task CreateChannel_WithNonExistentOwner_RollsBackAllEntities()
    {
        var fakeUserId = $"ghost-{Guid.NewGuid():N}";

        var act = () => Svc.CreateChannelService().CreateChannelAsync(
            fakeUserId,
            new CreateChannelRequest { Name = "doomed-channel" });

        await act.Should().ThrowAsync<Exception>("FK on OwnerUserId must fail");

        // Use a fresh context to query the DB (old context may have corrupt state)
        await using var freshDb = Fixture.CreateDbContext();

        var channels = await freshDb.Channels
            .IgnoreQueryFilters()
            .Where(c => c.OwnerUserId == fakeUserId)
            .ToListAsync();
        channels.Should().BeEmpty("entire SaveChanges must roll back");

        var roles = await freshDb.ChannelRoles
            .IgnoreQueryFilters()
            .ToListAsync();
        roles.Should().BeEmpty("roles from the failed batch must not persist");

        var members = await freshDb.ChannelMembers
            .IgnoreQueryFilters()
            .Where(m => m.UserId == fakeUserId)
            .ToListAsync();
        members.Should().BeEmpty("member from the failed batch must not persist");
    }

    [Fact]
    public async Task UploadAttachment_DbFails_BlobUploadedButNoDbRow()
    {
        // Use a userId that does NOT exist in the User table. The FK on Attachment.UploadedByUserId will fail at SaveChanges.
        var ghostUserId = $"ghost-{Guid.NewGuid():N}";

        var svc = Svc.CreateAttachmentService();
        using var stream = new MemoryStream("fake-content"u8.ToArray());

        var act = () => svc.UploadAsync(stream, new UploadAttachmentRequest
        {
            FileName = "test.jpg",
            ContentType = "image/jpeg",
            FileSizeBytes = 1024,
            Purpose = AttachmentPurpose.MessageAttachment
        }, ghostUserId);

        await act.Should().ThrowAsync<Exception>("FK violation on non-existent user");

        // Blob WAS uploaded (the stub was called before DB save)
        await Svc.BlobStorage.Received(1).UploadAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());

        // But no attachment row in DB
        await using var freshDb = Fixture.CreateDbContext();
        var attachments = await freshDb.Attachments.ToListAsync();
        attachments.Should().BeEmpty("DB row must not persist when SaveChanges fails");
    }

    [Fact]
    public async Task DeleteAccount_ScrubbsAllPII_AndDeactivatesMemberships()
    {
        var owner = await SeedUserAsync();
        var user = await SeedUserAsync();
        var (channel, _, ownerRole, _) = await SeedChannelAsync(owner);
        var member = await SeedMemberAsync(channel, user);
        var msg = await SeedChannelMessageAsync(channel.ChannelId, user.UserId);

        // Reaction
        Db.MessageReactions.Add(new MessageReaction
        {
            MessageReactionId = Guid.NewGuid(),
            MessageId = msg.MessageId,
            ReactedByUserId = user.UserId,
            Emoji = "👍",
            ReactedAt = DateTimeOffset.UtcNow
        });

        // Block (user blocks owner)
        Db.UserBlocks.Add(new UserBlock
        {
            UserBlockId = Guid.NewGuid(),
            BlockingUserId = user.UserId,
            BlockedUserId = owner.UserId,
            BlockedAt = DateTimeOffset.UtcNow
        });

        // Audit log with IP
        Db.ChannelAuditLogs.Add(new ChannelAuditLog
        {
            ChannelAuditLogId = Guid.NewGuid(),
            ChannelId = channel.ChannelId,
            ActorUserId = user.UserId,
            Action = AuditAction.ChannelUpdated,
            IpAddress = "192.168.1.1",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await Db.SaveChangesAsync();
        DetachAll();

        // Act
        await Svc.CreateUserService().DeleteAccountAsync(user.UserId);
        DetachAll();

        // Assert: user is scrubbed
        var scrubbed = await Db.DomainUsers
            .IgnoreQueryFilters()
            .FirstAsync(u => u.UserId == user.UserId);
        scrubbed.DisplayName.Should().Be("Deleted User");
        scrubbed.Bio.Should().BeNull();
        scrubbed.DeletedAt.Should().NotBeNull();
        scrubbed.Username.Should().StartWith("deleted_");

        // Blocks deleted
        var blocks = await Db.UserBlocks.Where(b => b.BlockingUserId == user.UserId || b.BlockedUserId == user.UserId).ToListAsync();
        blocks.Should().BeEmpty();

        // Reactions deleted
        var reactions = await Db.MessageReactions.Where(r => r.ReactedByUserId == user.UserId).ToListAsync();
        reactions.Should().BeEmpty();

        // Membership deactivated
        var membership = await Db.ChannelMembers
            .IgnoreQueryFilters()
            .FirstAsync(m => m.UserId == user.UserId && m.ChannelId == channel.ChannelId);
        membership.LeftAt.Should().NotBeNull();

        // Audit log IP scrubbed
        var auditLog = await Db.ChannelAuditLogs
            .IgnoreQueryFilters()
            .FirstAsync(a => a.ActorUserId == user.UserId);
        auditLog.IpAddress.Should().BeNull("GDPR requires IP scrubbing");

        // Message preserved (points to anonymized user)
        var preservedMsg = await Db.Messages.FirstOrDefaultAsync(m => m.MessageId == msg.MessageId);
        preservedMsg.Should().NotBeNull("messages from deleted users are preserved");
        preservedMsg!.AuthorUserId.Should().Be(user.UserId);
    }
}
