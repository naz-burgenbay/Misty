using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;

namespace Misty.Application.Services;

public class ConversationService : IConversationService
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserBlockService _userBlockService;
    private readonly IBlobStorageProvider _blobStorage;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IConversationRepository conversationRepository,
        IUserRepository userRepository,
        IUserBlockService userBlockService,
        IBlobStorageProvider blobStorage,
        ILogger<ConversationService> logger)
    {
        _conversationRepository = conversationRepository;
        _userRepository = userRepository;
        _userBlockService = userBlockService;
        _blobStorage = blobStorage;
        _logger = logger;
    }

    // UC-3.1 Get or Create Conversation
    public async Task<ConversationDetailResponse> GetOrCreateConversationAsync(
        string userId, string otherUserId, CancellationToken ct = default)
    {
        if (userId == otherUserId)
            throw new BusinessRuleException("Cannot start a conversation with yourself.");

        var otherUser = await _userRepository.GetByIdAsync(otherUserId, ct)
            ?? throw new NotFoundException("User", otherUserId);

        await _userBlockService.EnsureNotBlockedAsync(userId, otherUserId, ct);

        var conversation = await _conversationRepository.GetByParticipantsAsync(userId, otherUserId, ct);

        if (conversation is not null)
        {
            // Resurface if caller had hidden
            var callerParticipant = conversation.Participants.First(p => p.UserId == userId);
            if (callerParticipant.HiddenAt is not null)
            {
                callerParticipant.HiddenAt = null;
                await _conversationRepository.SaveChangesAsync(ct);
            }

            _logger.LogInformation("Existing conversation {ConversationId} returned for {UserId} and {OtherUserId}",
                conversation.ConversationId, userId, otherUserId);

            return await ToDetailResponseAsync(conversation, userId, ct);
        }

        var now = DateTimeOffset.UtcNow;
        var (low, high) = Conversation.NormalizeParticipants(userId, otherUserId);

        conversation = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            CreatedAt = now,
            ParticipantLowUserId = low,
            ParticipantHighUserId = high,
            Participants =
            {
                new ConversationParticipant { UserId = userId, JoinedAt = now },
                new ConversationParticipant { UserId = otherUserId, JoinedAt = now }
            }
        };

        try
        {
            await _conversationRepository.CreateConversationAsync(conversation, ct);
        }
        catch (DuplicateException)
        {
            conversation = await _conversationRepository.GetByParticipantsAsync(userId, otherUserId, ct)
                ?? throw new InvalidOperationException(
                    "Conversation was created concurrently but could not be retrieved.");

            var callerParticipant = conversation.Participants.First(p => p.UserId == userId);
            if (callerParticipant.HiddenAt is not null)
            {
                callerParticipant.HiddenAt = null;
                await _conversationRepository.SaveChangesAsync(ct);
            }

            _logger.LogInformation(
                "Concurrent create resolved — existing conversation {ConversationId} returned for {UserId} and {OtherUserId}",
                conversation.ConversationId, userId, otherUserId);

            return await ToDetailResponseAsync(conversation, userId, ct);
        }

        _logger.LogInformation("Conversation {ConversationId} created between {UserId} and {OtherUserId}",
            conversation.ConversationId, userId, otherUserId);

        return await ToDetailResponseAsync(conversation, userId, ct);
    }

    // UC-3.2 List Conversations
    public async Task<IReadOnlyList<ConversationSummary>> GetConversationsAsync(
        string userId, CancellationToken ct = default)
    {
        var conversations = await _conversationRepository.GetVisibleConversationsAsync(userId, ct);

        var summaries = new List<ConversationSummary>(conversations.Count);
        foreach (var conversation in conversations)
        {
            var otherParticipant = conversation.Participants.First(p => p.UserId != userId);
            var callerParticipant = conversation.Participants.First(p => p.UserId == userId);
            var unreadCount = await _conversationRepository.GetUnreadCountAsync(
                conversation.ConversationId, userId, callerParticipant.LastReadAt, ct);

            var lastMessage = conversation.Messages
                .OrderByDescending(m => m.SentAt)
                .FirstOrDefault();

            summaries.Add(new ConversationSummary
            {
                ConversationId = conversation.ConversationId,
                OtherParticipant = await ToUserSummaryAsync(otherParticipant.User, ct),
                LastMessage = lastMessage is not null
                    ? new MessageSummary
                    {
                        MessageId = lastMessage.MessageId,
                        AuthorDisplayName = lastMessage.Author.DisplayName,
                        Content = lastMessage.Content,
                        SentAt = lastMessage.SentAt
                    }
                    : null,
                UnreadCount = unreadCount
            });
        }

        return summaries;
    }

    // UC-3.3 Get Conversation Detail
    public async Task<ConversationDetailResponse> GetConversationAsync(
        Guid conversationId, string userId, CancellationToken ct = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);

        EnsureParticipant(conversation, userId);

        return await ToDetailResponseAsync(conversation, userId, ct);
    }

    // UC-3.4 Hide Conversation
    public async Task HideConversationAsync(
        Guid conversationId, string userId, CancellationToken ct = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);

        var participant = EnsureParticipant(conversation, userId);
        participant.HiddenAt = DateTimeOffset.UtcNow;

        await _conversationRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Conversation {ConversationId} hidden by {UserId}",
            conversationId, userId);
    }

    // UC-3.5 Mark Conversation as Read
    public async Task MarkConversationReadAsync(
        Guid conversationId, string userId, DateTimeOffset lastReadAt, CancellationToken ct = default)
    {
        var conversation = await _conversationRepository.GetByIdAsync(conversationId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);

        var participant = EnsureParticipant(conversation, userId);
        participant.LastReadAt = lastReadAt;

        await _conversationRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Conversation {ConversationId} marked as read by {UserId}",
            conversationId, userId);
    }

    private static ConversationParticipant EnsureParticipant(Conversation conversation, string userId)
    {
        return conversation.Participants.FirstOrDefault(p => p.UserId == userId)
            ?? throw new NotFoundException("Conversation", conversation.ConversationId);
    }

    private async Task<ConversationDetailResponse> ToDetailResponseAsync(
        Conversation conversation, string userId, CancellationToken ct)
    {
        var otherParticipant = conversation.Participants.First(p => p.UserId != userId);
        var otherUser = otherParticipant.User
            ?? await _userRepository.GetByIdAsync(otherParticipant.UserId, ct);

        return new ConversationDetailResponse
        {
            ConversationId = conversation.ConversationId,
            OtherParticipant = await ToUserSummaryAsync(otherUser!, ct),
            CreatedAt = conversation.CreatedAt
        };
    }

    private async Task<UserSummary> ToUserSummaryAsync(User user, CancellationToken ct)
    {
        string? avatarUrl = null;
        if (user.Avatar is not null)
            avatarUrl = await _blobStorage.GetDownloadUrlAsync(user.Avatar.StoragePath, ct);

        return new UserSummary
        {
            Id = user.UserId,
            DisplayName = user.DisplayName,
            AvatarUrl = avatarUrl
        };
    }
}
