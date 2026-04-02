using FluentAssertions;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Application.Services;
using Misty.Domain.Entities;
using Misty.Tests.Common;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Misty.Tests.Application.Services;

public class ConversationServiceTests
{
    private readonly IConversationRepository _conversationRepo = Substitute.For<IConversationRepository>();
    private readonly IUserRepository _userRepo = Substitute.For<IUserRepository>();
    private readonly IUserBlockService _blockService = Substitute.For<IUserBlockService>();
    private readonly IBlobStorageProvider _blobStorage = Substitute.For<IBlobStorageProvider>();
    private readonly ConversationService _sut;

    private readonly User _userA;
    private readonly User _userB;

    public ConversationServiceTests()
    {
        _sut = new ConversationService(
            _conversationRepo, _userRepo, _blockService, _blobStorage,
            Substitute.For<ILogger<ConversationService>>());

        _userA = TestData.User(displayName: "Alice");
        _userB = TestData.User(displayName: "Bob");

        _userRepo.GetByIdAsync(_userA.UserId, Arg.Any<CancellationToken>()).Returns(_userA);
        _userRepo.GetByIdAsync(_userB.UserId, Arg.Any<CancellationToken>()).Returns(_userB);
    }

    // UC-3.1 Get or Create Conversation

    [Fact]
    public async Task GetOrCreate_NewConversation_CreatesAndReturnsDetail()
    {
        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var result = await _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        result.OtherParticipant.Id.Should().Be(_userB.UserId);
        result.OtherParticipant.DisplayName.Should().Be(_userB.DisplayName);
        await _conversationRepo.Received(1).CreateConversationAsync(
            Arg.Is<Conversation>(c =>
                c.Participants.Count == 2 &&
                c.Participants.Any(p => p.UserId == _userA.UserId) &&
                c.Participants.Any(p => p.UserId == _userB.UserId)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreate_NewConversation_NormalizesParticipantIds()
    {
        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        await _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        var (expectedLow, expectedHigh) = Conversation.NormalizeParticipants(_userA.UserId, _userB.UserId);
        await _conversationRepo.Received(1).CreateConversationAsync(
            Arg.Is<Conversation>(c =>
                c.ParticipantLowUserId == expectedLow &&
                c.ParticipantHighUserId == expectedHigh),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreate_ExistingConversation_ReturnsSameConversation()
    {
        var existing = CreateConversation();
        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns(existing);

        var result = await _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        result.ConversationId.Should().Be(existing.ConversationId);
        await _conversationRepo.DidNotReceive().CreateConversationAsync(
            Arg.Any<Conversation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreate_ExistingHidden_ResurfacesConversation()
    {
        var existing = CreateConversation();
        var callerParticipant = existing.Participants.First(p => p.UserId == _userA.UserId);
        callerParticipant.HiddenAt = DateTimeOffset.UtcNow.AddDays(-1);

        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns(existing);

        await _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        callerParticipant.HiddenAt.Should().BeNull();
        await _conversationRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreate_WithSelf_ThrowsBusinessRule()
    {
        var act = () => _sut.GetOrCreateConversationAsync(_userA.UserId, _userA.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetOrCreate_OtherUserNotFound_ThrowsNotFound()
    {
        _userRepo.GetByIdAsync("ghost", Arg.Any<CancellationToken>()).Returns((User?)null);

        var act = () => _sut.GetOrCreateConversationAsync(_userA.UserId, "ghost");

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetOrCreate_Blocked_ThrowsBusinessRule()
    {
        _blockService.EnsureNotBlockedAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BusinessRuleException("Interaction is blocked between these users."));

        var act = () => _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentCreate_ReturnsExistingConversation()
    {
        var existing = CreateConversation();

        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null, existing);

        _conversationRepo.CreateConversationAsync(Arg.Any<Conversation>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateException("Conversation already exists."));

        var result = await _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        result.ConversationId.Should().Be(existing.ConversationId);
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentCreate_ResurfacesIfHidden()
    {
        var existing = CreateConversation();
        var callerParticipant = existing.Participants.First(p => p.UserId == _userA.UserId);
        callerParticipant.HiddenAt = DateTimeOffset.UtcNow.AddDays(-1);

        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null, existing);

        _conversationRepo.CreateConversationAsync(Arg.Any<Conversation>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateException("Conversation already exists."));

        await _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        callerParticipant.HiddenAt.Should().BeNull();
        await _conversationRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentCreate_RetrieveFails_ThrowsInvalidOperation()
    {
        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null, (Conversation?)null);

        _conversationRepo.CreateConversationAsync(Arg.Any<Conversation>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateException("Conversation already exists."));

        var act = () => _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetOrCreate_ConcurrentCreate_RetrievedConversationMissingCaller_Throws()
    {
        // Repo returns a conversation whose Participants list does not include the caller
        var stranger = TestData.User(displayName: "Stranger");
        var malformed = TestData.Conversation(stranger.UserId, _userB.UserId);
        foreach (var p in malformed.Participants)
            p.User = p.UserId == _userB.UserId ? _userB : stranger;

        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null, malformed);

        _conversationRepo.CreateConversationAsync(Arg.Any<Conversation>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DuplicateException("Conversation already exists."));

        var act = () => _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // UC-3.2 List Conversations

    [Fact]
    public async Task GetConversations_ReturnsVisibleConversations()
    {
        var conversation = CreateConversation();
        conversation.Messages.Add(new Message
        {
            MessageId = Guid.NewGuid(),
            ConversationId = conversation.ConversationId,
            AuthorUserId = _userB.UserId,
            Author = _userB,
            Content = "Hello!",
            SentAt = DateTimeOffset.UtcNow
        });

        _conversationRepo.GetVisibleConversationsAsync(_userA.UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Conversation> { conversation });
        _conversationRepo.GetUnreadCountAsync(conversation.ConversationId, _userA.UserId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(1);

        var result = await _sut.GetConversationsAsync(_userA.UserId);

        result.Should().HaveCount(1);
        result[0].ConversationId.Should().Be(conversation.ConversationId);
        result[0].OtherParticipant.Id.Should().Be(_userB.UserId);
        result[0].LastMessage.Should().NotBeNull();
        result[0].LastMessage!.Content.Should().Be("Hello!");
        result[0].UnreadCount.Should().Be(1);
    }

    [Fact]
    public async Task GetConversations_NoConversations_ReturnsEmpty()
    {
        _conversationRepo.GetVisibleConversationsAsync(_userA.UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Conversation>());

        var result = await _sut.GetConversationsAsync(_userA.UserId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConversations_NoMessages_LastMessageIsNull()
    {
        var conversation = CreateConversation();
        _conversationRepo.GetVisibleConversationsAsync(_userA.UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Conversation> { conversation });
        _conversationRepo.GetUnreadCountAsync(conversation.ConversationId, _userA.UserId, Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await _sut.GetConversationsAsync(_userA.UserId);

        result[0].LastMessage.Should().BeNull();
        result[0].UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task GetConversations_NullLastReadAt_PassedToUnreadCount()
    {
        var conversation = CreateConversation();
        var callerParticipant = conversation.Participants.First(p => p.UserId == _userA.UserId);
        callerParticipant.LastReadAt = null;

        _conversationRepo.GetVisibleConversationsAsync(_userA.UserId, Arg.Any<CancellationToken>())
            .Returns(new List<Conversation> { conversation });
        _conversationRepo.GetUnreadCountAsync(conversation.ConversationId, _userA.UserId, null, Arg.Any<CancellationToken>())
            .Returns(5);

        var result = await _sut.GetConversationsAsync(_userA.UserId);

        result[0].UnreadCount.Should().Be(5);
        await _conversationRepo.Received(1).GetUnreadCountAsync(
            conversation.ConversationId, _userA.UserId, null, Arg.Any<CancellationToken>());
    }

    // UC-3.3 Get Conversation Detail

    [Fact]
    public async Task GetConversation_AsParticipant_ReturnsDetail()
    {
        var conversation = CreateConversation();
        _conversationRepo.GetByIdAsync(conversation.ConversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        var result = await _sut.GetConversationAsync(conversation.ConversationId, _userA.UserId);

        result.ConversationId.Should().Be(conversation.ConversationId);
        result.OtherParticipant.Id.Should().Be(_userB.UserId);
        result.OtherParticipant.DisplayName.Should().Be(_userB.DisplayName);
    }

    [Fact]
    public async Task GetConversation_NotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _conversationRepo.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var act = () => _sut.GetConversationAsync(id, _userA.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetConversation_NotParticipant_ThrowsNotFound()
    {
        var conversation = CreateConversation();
        _conversationRepo.GetByIdAsync(conversation.ConversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        var outsider = TestData.User(displayName: "Naz");
        var act = () => _sut.GetConversationAsync(conversation.ConversationId, outsider.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-3.4 Hide Conversation

    [Fact]
    public async Task HideConversation_SetsHiddenAt()
    {
        var conversation = CreateConversation();
        _conversationRepo.GetByIdAsync(conversation.ConversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        await _sut.HideConversationAsync(conversation.ConversationId, _userA.UserId);

        var participant = conversation.Participants.First(p => p.UserId == _userA.UserId);
        participant.HiddenAt.Should().NotBeNull();
        participant.HiddenAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        await _conversationRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HideConversation_NotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _conversationRepo.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var act = () => _sut.HideConversationAsync(id, _userA.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task HideConversation_NotParticipant_ThrowsNotFound()
    {
        var conversation = CreateConversation();
        _conversationRepo.GetByIdAsync(conversation.ConversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        var outsider = TestData.User(displayName: "Naz");
        var act = () => _sut.HideConversationAsync(conversation.ConversationId, outsider.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // UC-3.5 Mark Conversation as Read

    [Fact]
    public async Task MarkRead_SetsLastReadAt()
    {
        var conversation = CreateConversation();
        _conversationRepo.GetByIdAsync(conversation.ConversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        var readAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await _sut.MarkConversationReadAsync(conversation.ConversationId, _userA.UserId, readAt);

        var participant = conversation.Participants.First(p => p.UserId == _userA.UserId);
        participant.LastReadAt.Should().Be(readAt);
        await _conversationRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MarkRead_FutureTimestamp_ClampsToNow()
    {
        var conversation = CreateConversation();
        _conversationRepo.GetByIdAsync(conversation.ConversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        var future = DateTimeOffset.UtcNow.AddHours(1);
        await _sut.MarkConversationReadAsync(conversation.ConversationId, _userA.UserId, future);

        var participant = conversation.Participants.First(p => p.UserId == _userA.UserId);
        participant.LastReadAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        participant.LastReadAt.Should().BeBefore(future);
    }

    [Fact]
    public async Task MarkRead_NotFound_ThrowsNotFound()
    {
        var id = Guid.NewGuid();
        _conversationRepo.GetByIdAsync(id, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        var act = () => _sut.MarkConversationReadAsync(id, _userA.UserId, DateTimeOffset.UtcNow);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task MarkRead_NotParticipant_ThrowsNotFound()
    {
        var conversation = CreateConversation();
        _conversationRepo.GetByIdAsync(conversation.ConversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        var outsider = TestData.User(displayName: "Naz");
        var act = () => _sut.MarkConversationReadAsync(conversation.ConversationId, outsider.UserId, DateTimeOffset.UtcNow);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task MarkRead_OlderTimestamp_AcceptedByService()
    {
        var conversation = CreateConversation();
        var participant = conversation.Participants.First(p => p.UserId == _userA.UserId);
        participant.LastReadAt = DateTimeOffset.UtcNow.AddMinutes(-2);

        _conversationRepo.GetByIdAsync(conversation.ConversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        var olderTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        await _sut.MarkConversationReadAsync(conversation.ConversationId, _userA.UserId, olderTimestamp);

        // Service does NOT enforce monotonicity — it accepts older timestamps
        participant.LastReadAt.Should().Be(olderTimestamp);
    }

    // Multi-step scenarios

    [Fact]
    public async Task HideThenGetOrCreate_ResurfacesConversation()
    {
        var conversation = CreateConversation();
        _conversationRepo.GetByIdAsync(conversation.ConversationId, Arg.Any<CancellationToken>())
            .Returns(conversation);
        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        // Step 1: Hide
        await _sut.HideConversationAsync(conversation.ConversationId, _userA.UserId);
        conversation.Participants.First(p => p.UserId == _userA.UserId).HiddenAt.Should().NotBeNull();

        // Step 2: GetOrCreate resurfaces
        await _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);
        conversation.Participants.First(p => p.UserId == _userA.UserId).HiddenAt.Should().BeNull();
    }

    [Fact]
    public async Task GetOrCreate_OtherUserStillHidden_DoesNotAffectThem()
    {
        var conversation = CreateConversation();
        var callerParticipant = conversation.Participants.First(p => p.UserId == _userA.UserId);
        var otherParticipant = conversation.Participants.First(p => p.UserId == _userB.UserId);
        callerParticipant.HiddenAt = DateTimeOffset.UtcNow.AddDays(-1);
        otherParticipant.HiddenAt = DateTimeOffset.UtcNow.AddDays(-2);

        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns(conversation);

        await _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        callerParticipant.HiddenAt.Should().BeNull();
        otherParticipant.HiddenAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrCreate_BlockCheckHappensBeforeCreate()
    {
        // Ensure block check runs even when no existing conversation
        _conversationRepo.GetByParticipantsAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .Returns((Conversation?)null);

        _blockService.EnsureNotBlockedAsync(_userA.UserId, _userB.UserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new BusinessRuleException("Blocked"));

        var act = () => _sut.GetOrCreateConversationAsync(_userA.UserId, _userB.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
        await _conversationRepo.DidNotReceive().CreateConversationAsync(
            Arg.Any<Conversation>(), Arg.Any<CancellationToken>());
    }

    // Helper

    private Conversation CreateConversation()
    {
        var conv = TestData.Conversation(_userA.UserId, _userB.UserId);
        // Wire up User navigation properties for the service's ToUserSummary calls
        foreach (var p in conv.Participants)
        {
            p.User = p.UserId == _userA.UserId ? _userA : _userB;
        }
        return conv;
    }
}
