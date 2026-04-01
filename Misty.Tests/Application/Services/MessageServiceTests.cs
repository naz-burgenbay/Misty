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

namespace Misty.Tests.Application.Services;

public class MessageServiceTests
{
    private readonly IMessageRepository _messageRepo = Substitute.For<IMessageRepository>();
    private readonly IUserBlockService _blockService = Substitute.For<IUserBlockService>();
    private readonly IBlobStorageProvider _blobStorage = Substitute.For<IBlobStorageProvider>();
    private readonly IValidator<SendMessageRequest> _sendValidator = Substitute.For<IValidator<SendMessageRequest>>();
    private readonly IValidator<UpdateMessageRequest> _updateValidator = Substitute.For<IValidator<UpdateMessageRequest>>();
    private readonly IValidator<AddReactionRequest> _reactionValidator = Substitute.For<IValidator<AddReactionRequest>>();
    private readonly MessageService _sut;

    private readonly User _owner;
    private readonly User _sender;
    private readonly User _otherUser;
    private readonly Channel _channel;
    private readonly ChannelMember _senderMember;

    public MessageServiceTests()
    {
        _sut = new MessageService(
            _messageRepo, _blockService, _blobStorage,
            _sendValidator, _updateValidator, _reactionValidator,
            Substitute.For<ILogger<MessageService>>());

        // Validators pass by default
        _sendValidator.ValidateAsync(Arg.Any<ValidationContext<SendMessageRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _updateValidator.ValidateAsync(Arg.Any<ValidationContext<UpdateMessageRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        _reactionValidator.ValidateAsync(Arg.Any<ValidationContext<AddReactionRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        _owner = TestData.User(displayName: "Owner");
        _sender = TestData.User(displayName: "Sender");
        _otherUser = TestData.User(displayName: "Other");
        _channel = TestData.Channel(_owner.UserId,
            ChannelPermission.SendMessages | ChannelPermission.AddReactions | ChannelPermission.AttachFiles);

        _senderMember = TestData.Member(_channel, _sender);

        // Default wiring
        _messageRepo.GetChannelMemberAsync(_channel.ChannelId, _sender.UserId, Arg.Any<CancellationToken>())
            .Returns(_senderMember);
        _messageRepo.IsUserMutedAsync(_channel.ChannelId, _sender.UserId, Arg.Any<CancellationToken>())
            .Returns(false);
        _messageRepo.GetByIdempotencyKeyAsync(_sender.UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Message?)null);
    }

    // Helpers

    private SendMessageRequest NewSendRequest(string? idempotencyKey = null, Guid? parentMessageId = null, IReadOnlyList<Guid>? attachmentIds = null) => new()
    {
        Content = "Hello world",
        IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString(),
        ParentMessageId = parentMessageId,
        AttachmentIds = attachmentIds
    };

    private Message SavedMessage(Guid? channelId = null, Guid? conversationId = null, string? authorUserId = null)
    {
        var authorId = authorUserId ?? _sender.UserId;
        return new Message
        {
            MessageId = Guid.NewGuid(),
            ChannelId = channelId,
            ConversationId = conversationId,
            AuthorUserId = authorId,
            Author = authorId == _sender.UserId ? _sender : _otherUser,
            Content = "Hello world",
            SentAt = DateTimeOffset.UtcNow,
            Attachments = new List<Attachment>(),
            Reactions = new List<MessageReaction>()
        };
    }

    private Attachment AttachmentForSender(Guid? id = null, bool claimed = false, AttachmentPurpose purpose = AttachmentPurpose.MessageAttachment)
    {
        return new Attachment
        {
            AttachmentId = id ?? Guid.NewGuid(),
            UploadedByUserId = _sender.UserId,
            Purpose = purpose,
            MessageId = claimed ? Guid.NewGuid() : null,
            FileName = "file.png",
            StoragePath = "path/file.png",
            ContentType = "image/png"
        };
    }

    private void SetupSaveReturnsMessage(Guid? channelId = null, Guid? conversationId = null)
    {
        _messageRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => SavedMessage(channelId, conversationId));
    }

    // UC-4.1 Send Channel Message

    [Fact]
    public async Task SendChannelMessage_ValidRequest_Succeeds()
    {
        SetupSaveReturnsMessage(channelId: _channel.ChannelId);

        var result = await _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId, NewSendRequest());

        result.Content.Should().Be("Hello world");
        await _messageRepo.Received(1).AddAsync(
            Arg.Is<Message>(m => m.ChannelId == _channel.ChannelId && m.AuthorUserId == _sender.UserId),
            Arg.Any<CancellationToken>());
        await _messageRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendChannelMessage_WithoutSendPermission_Throws()
    {
        var channel = TestData.Channel(_owner.UserId, ChannelPermission.None);
        var member = TestData.Member(channel, _sender);

        _messageRepo.GetChannelMemberAsync(channel.ChannelId, _sender.UserId, Arg.Any<CancellationToken>())
            .Returns(member);

        var act = () => _sut.SendChannelMessageAsync(
            channel.ChannelId, _sender.UserId, NewSendRequest());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendChannelMessage_MutedUser_Throws()
    {
        _messageRepo.IsUserMutedAsync(_channel.ChannelId, _sender.UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        var act = () => _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId, NewSendRequest());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendChannelMessage_Idempotency_ReturnsExisting()
    {
        var existing = SavedMessage(channelId: _channel.ChannelId);
        _messageRepo.GetByIdempotencyKeyAsync(_sender.UserId, "key-123", Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId, NewSendRequest(idempotencyKey: "key-123"));

        result.MessageId.Should().Be(existing.MessageId);
        await _messageRepo.DidNotReceive().AddAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendChannelMessage_ParentInDifferentChannel_Throws()
    {
        var parentId = Guid.NewGuid();
        var parentMessage = SavedMessage(channelId: Guid.NewGuid()); // different channel
        _messageRepo.GetByIdAsync(parentId, Arg.Any<CancellationToken>())
            .Returns(parentMessage);

        var act = () => _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId, NewSendRequest(parentMessageId: parentId));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendChannelMessage_ParentNotFound_Throws()
    {
        var parentId = Guid.NewGuid();
        _messageRepo.GetByIdAsync(parentId, Arg.Any<CancellationToken>())
            .Returns((Message?)null);

        var act = () => _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId, NewSendRequest(parentMessageId: parentId));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SendChannelMessage_AttachmentNotFound_Throws()
    {
        _messageRepo.GetAttachmentsByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Attachment>());

        var act = () => _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId,
            NewSendRequest(attachmentIds: [Guid.NewGuid()]));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SendChannelMessage_AttachmentNotOwned_Throws()
    {
        var attachmentId = Guid.NewGuid();
        var attachment = new Attachment
        {
            AttachmentId = attachmentId,
            UploadedByUserId = _otherUser.UserId, // not the sender
            Purpose = AttachmentPurpose.MessageAttachment,
            MessageId = null,
            FileName = "file.png",
            StoragePath = "path",
            ContentType = "image/png"
        };

        _messageRepo.GetAttachmentsByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Attachment> { attachment });

        var act = () => _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId,
            NewSendRequest(attachmentIds: [attachmentId]));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendChannelMessage_AttachmentAlreadyClaimed_Throws()
    {
        var attachmentId = Guid.NewGuid();
        var attachment = AttachmentForSender(attachmentId, claimed: true);

        _messageRepo.GetAttachmentsByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Attachment> { attachment });

        var act = () => _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId,
            NewSendRequest(attachmentIds: [attachmentId]));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendChannelMessage_AttachmentWrongPurpose_Throws()
    {
        var attachmentId = Guid.NewGuid();
        var attachment = AttachmentForSender(attachmentId, purpose: AttachmentPurpose.UserAvatar);

        _messageRepo.GetAttachmentsByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Attachment> { attachment });

        var act = () => _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId,
            NewSendRequest(attachmentIds: [attachmentId]));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendChannelMessage_AttachmentsAreClaimed()
    {
        var attachmentId = Guid.NewGuid();
        var attachment = AttachmentForSender(attachmentId);

        _messageRepo.GetAttachmentsByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Attachment> { attachment });
        _messageRepo.ClaimAttachmentsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(1);
        SetupSaveReturnsMessage(channelId: _channel.ChannelId);

        await _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId,
            NewSendRequest(attachmentIds: [attachmentId]));

        await _messageRepo.Received(1).ClaimAttachmentsAsync(
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(attachmentId)),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendChannelMessage_UpdatesLastMessageAt()
    {
        var before = _channel.LastMessageAt;
        SetupSaveReturnsMessage(channelId: _channel.ChannelId);

        await _sut.SendChannelMessageAsync(
            _channel.ChannelId, _sender.UserId, NewSendRequest());

        _channel.LastMessageAt.Should().NotBe(before);
    }

    [Fact]
    public async Task SendChannelMessage_WithoutAttachFilesPermission_Throws()
    {
        var channel = TestData.Channel(_owner.UserId, ChannelPermission.SendMessages); // no AttachFiles
        var member = TestData.Member(channel, _sender);

        _messageRepo.GetChannelMemberAsync(channel.ChannelId, _sender.UserId, Arg.Any<CancellationToken>())
            .Returns(member);

        var act = () => _sut.SendChannelMessageAsync(
            channel.ChannelId, _sender.UserId,
            NewSendRequest(attachmentIds: [Guid.NewGuid()]));

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendChannelMessage_NotMember_ThrowsNotFound()
    {
        _messageRepo.GetChannelMemberAsync(_channel.ChannelId, "unknown", Arg.Any<CancellationToken>())
            .Returns((ChannelMember?)null);

        var act = () => _sut.SendChannelMessageAsync(
            _channel.ChannelId, "unknown", NewSendRequest());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-4.2 Send Conversation Message

    [Fact]
    public async Task SendConversationMessage_ValidRequest_Succeeds()
    {
        var conversationId = Guid.NewGuid();
        SetupConversation(conversationId);
        SetupSaveReturnsMessage(conversationId: conversationId);

        var result = await _sut.SendConversationMessageAsync(
            conversationId, _sender.UserId, NewSendRequest());

        result.Content.Should().Be("Hello world");
        await _messageRepo.Received(1).AddAsync(
            Arg.Is<Message>(m => m.ConversationId == conversationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendConversationMessage_BlockedUsers_Throws()
    {
        var conversationId = Guid.NewGuid();
        SetupConversation(conversationId);

        _blockService.EnsureNotBlockedAsync(_sender.UserId, _otherUser.UserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new BusinessRuleException("Interaction is blocked between these users.")));

        var act = () => _sut.SendConversationMessageAsync(
            conversationId, _sender.UserId, NewSendRequest());

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task SendConversationMessage_HiddenConversation_GetsUnhidden()
    {
        var conversationId = Guid.NewGuid();
        var otherParticipant = SetupConversation(conversationId);
        otherParticipant.HiddenAt = DateTimeOffset.UtcNow.AddDays(-1); // was hidden
        SetupSaveReturnsMessage(conversationId: conversationId);

        await _sut.SendConversationMessageAsync(
            conversationId, _sender.UserId, NewSendRequest());

        otherParticipant.HiddenAt.Should().BeNull("sending a message should unhide the conversation");
    }

    [Fact]
    public async Task SendConversationMessage_Idempotency_ReturnsExisting()
    {
        var conversationId = Guid.NewGuid();
        SetupConversation(conversationId);

        var existing = SavedMessage(conversationId: conversationId);
        _messageRepo.GetByIdempotencyKeyAsync(_sender.UserId, "key", Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await _sut.SendConversationMessageAsync(
            conversationId, _sender.UserId, NewSendRequest(idempotencyKey: "key"));

        result.MessageId.Should().Be(existing.MessageId);
        await _messageRepo.DidNotReceive().AddAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendConversationMessage_NotParticipant_ThrowsNotFound()
    {
        _messageRepo.GetConversationParticipantAsync(Arg.Any<Guid>(), "unknown", Arg.Any<CancellationToken>())
            .Returns((ConversationParticipant?)null);

        var act = () => _sut.SendConversationMessageAsync(
            Guid.NewGuid(), "unknown", NewSendRequest());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-4.5 Update Message

    [Fact]
    public async Task UpdateMessage_Author_SucceedsAndSetsEditedAt()
    {
        var message = SavedMessage(channelId: _channel.ChannelId);
        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);

        var request = new UpdateMessageRequest { Content = "Updated content" };

        var result = await _sut.UpdateMessageAsync(message.MessageId, _sender.UserId, request);

        message.Content.Should().Be("Updated content");
        message.EditedAt.Should().NotBeNull();
        await _messageRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateMessage_NonAuthor_Throws()
    {
        var message = SavedMessage(channelId: _channel.ChannelId);
        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);

        var request = new UpdateMessageRequest { Content = "Hacked!" };

        var act = () => _sut.UpdateMessageAsync(message.MessageId, _otherUser.UserId, request);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task UpdateMessage_NotFound_Throws()
    {
        _messageRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Message?)null);

        var act = () => _sut.UpdateMessageAsync(Guid.NewGuid(), _sender.UserId,
            new UpdateMessageRequest { Content = "Test" });

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-4.6 Delete Message

    [Fact]
    public async Task DeleteMessage_Author_Succeeds()
    {
        var message = SavedMessage(channelId: _channel.ChannelId);
        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        _messageRepo.GetRepliesAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(new List<Message>());

        await _sut.DeleteMessageAsync(message.MessageId, _sender.UserId);

        await _messageRepo.Received(1).DeleteAsync(message);
        await _messageRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteMessage_ModeratorWithPermission_Succeeds()
    {
        var message = SavedMessage(channelId: _channel.ChannelId, authorUserId: _otherUser.UserId);
        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        _messageRepo.GetRepliesAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(new List<Message>());

        // Moderator has DeleteMessages
        var modUser = TestData.User(displayName: "Mod");
        var modMember = TestData.Member(_channel, modUser);
        var modRole = TestData.Role(_channel, "Moderator", ChannelPermission.DeleteMessages, 50);
        TestData.AssignRole(modMember, modRole);

        _messageRepo.GetChannelMemberAsync(_channel.ChannelId, modUser.UserId, Arg.Any<CancellationToken>())
            .Returns(modMember);

        await _sut.DeleteMessageAsync(message.MessageId, modUser.UserId);

        await _messageRepo.Received(1).DeleteAsync(message);
        // Audit log should be written for moderator delete
        await _messageRepo.Received(1).AddAuditLogAsync(
            Arg.Is<ChannelAuditLog>(log => log.Action == AuditAction.MessageDeleted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteMessage_NormalUserCannotDeleteOthers()
    {
        var message = SavedMessage(channelId: _channel.ChannelId, authorUserId: _otherUser.UserId);
        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);

        // Sender has no DeleteMessages permission
        _messageRepo.GetChannelMemberAsync(_channel.ChannelId, _sender.UserId, Arg.Any<CancellationToken>())
            .Returns(_senderMember);

        var act = () => _sut.DeleteMessageAsync(message.MessageId, _sender.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task DeleteMessage_AttachmentBlobsDeleted()
    {
        var message = SavedMessage(channelId: _channel.ChannelId);
        message.Attachments = new List<Attachment>
        {
            new() { AttachmentId = Guid.NewGuid(), StoragePath = "blob/file1.png", FileName = "f1", ContentType = "image/png" },
            new() { AttachmentId = Guid.NewGuid(), StoragePath = "blob/file2.jpg", FileName = "f2", ContentType = "image/jpeg" }
        };

        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        _messageRepo.GetRepliesAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(new List<Message>());

        await _sut.DeleteMessageAsync(message.MessageId, _sender.UserId);

        await _blobStorage.Received(1).DeleteAsync("blob/file1.png", Arg.Any<CancellationToken>());
        await _blobStorage.Received(1).DeleteAsync("blob/file2.jpg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteMessage_RepliesGetParentCleared()
    {
        var message = SavedMessage(channelId: _channel.ChannelId);
        var reply = SavedMessage(channelId: _channel.ChannelId);
        reply.ParentMessageId = message.MessageId;

        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        _messageRepo.GetRepliesAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(new List<Message> { reply });

        await _sut.DeleteMessageAsync(message.MessageId, _sender.UserId);

        reply.ParentMessageId.Should().BeNull("replies should be detached when parent is deleted");
    }

    // UC-4.7 Add Reaction

    [Fact]
    public async Task AddReaction_ValidChannelMessage_Succeeds()
    {
        var message = SavedMessage(channelId: _channel.ChannelId, authorUserId: _otherUser.UserId);
        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);

        var request = new AddReactionRequest { Emoji = "👍" };

        await _sut.AddReactionAsync(message.MessageId, _sender.UserId, request);

        await _messageRepo.Received(1).AddReactionAsync(
            Arg.Is<MessageReaction>(r => r.Emoji == "👍" && r.ReactedByUserId == _sender.UserId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddReaction_WithoutAddReactionsPermission_Throws()
    {
        var channel = TestData.Channel(_owner.UserId, ChannelPermission.SendMessages); // no AddReactions
        var member = TestData.Member(channel, _sender);
        var message = SavedMessage(channelId: channel.ChannelId, authorUserId: _otherUser.UserId);

        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);
        _messageRepo.GetChannelMemberAsync(channel.ChannelId, _sender.UserId, Arg.Any<CancellationToken>())
            .Returns(member);

        var act = () => _sut.AddReactionAsync(message.MessageId, _sender.UserId,
            new AddReactionRequest { Emoji = "👍" });

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task AddReaction_Duplicate_ThrowsDuplicateException()
    {
        var message = SavedMessage(channelId: _channel.ChannelId);
        _messageRepo.GetByIdAsync(message.MessageId, Arg.Any<CancellationToken>())
            .Returns(message);

        _messageRepo.When(x => x.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new DuplicateException("duplicate"));

        var act = () => _sut.AddReactionAsync(
            message.MessageId, _sender.UserId,
            new AddReactionRequest { Emoji = "👍" });

        await act.Should().ThrowAsync<DuplicateException>();
    }

    // UC-4.8 Remove Reaction

    [Fact]
    public async Task RemoveReaction_OwnReaction_Succeeds()
    {
        var reaction = new MessageReaction
        {
            MessageReactionId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            ReactedByUserId = _sender.UserId,
            Emoji = "👍",
            ReactedAt = DateTimeOffset.UtcNow
        };

        _messageRepo.GetReactionAsync(reaction.MessageId, _sender.UserId, "👍", Arg.Any<CancellationToken>())
            .Returns(reaction);

        await _sut.RemoveReactionAsync(reaction.MessageId, _sender.UserId, "👍");

        await _messageRepo.Received(1).DeleteReactionAsync(reaction);
        await _messageRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoveReaction_NotFound_Throws()
    {
        _messageRepo.GetReactionAsync(Arg.Any<Guid>(), _sender.UserId, "👍", Arg.Any<CancellationToken>())
            .Returns((MessageReaction?)null);

        var act = () => _sut.RemoveReactionAsync(Guid.NewGuid(), _sender.UserId, "👍");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // Conversation helpers

    private ConversationParticipant SetupConversation(Guid conversationId)
    {
        var senderParticipant = new ConversationParticipant
        {
            ConversationParticipantId = Guid.NewGuid(),
            ConversationId = conversationId,
            UserId = _sender.UserId,
            JoinedAt = DateTimeOffset.UtcNow
        };

        var otherParticipant = new ConversationParticipant
        {
            ConversationParticipantId = Guid.NewGuid(),
            ConversationId = conversationId,
            UserId = _otherUser.UserId,
            JoinedAt = DateTimeOffset.UtcNow
        };

        var conversation = new Conversation
        {
            ConversationId = conversationId,
            ParticipantLowUserId = _sender.UserId,
            ParticipantHighUserId = _otherUser.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _messageRepo.GetConversationParticipantAsync(conversationId, _sender.UserId, Arg.Any<CancellationToken>())
            .Returns(senderParticipant);
        _messageRepo.GetOtherConversationParticipantAsync(conversationId, _sender.UserId, Arg.Any<CancellationToken>())
            .Returns(otherParticipant);
        _messageRepo.GetConversationAsync(conversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);
        _messageRepo.GetByIdempotencyKeyAsync(_sender.UserId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Message?)null);

        return otherParticipant;
    }
}
