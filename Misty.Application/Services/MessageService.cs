using System.Text;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.DTOs.Common;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Application.Services;

public class MessageService : IMessageService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;
    private const char CursorSeparator = '|';

    private readonly IMessageRepository _messageRepository;
    private readonly IUserBlockService _userBlockService;
    private readonly IBlobStorageProvider _blobStorage;
    private readonly IValidator<SendMessageRequest> _sendValidator;
    private readonly IValidator<UpdateMessageRequest> _updateValidator;
    private readonly IValidator<AddReactionRequest> _reactionValidator;
    private readonly ILogger<MessageService> _logger;

    public MessageService(
        IMessageRepository messageRepository,
        IUserBlockService userBlockService,
        IBlobStorageProvider blobStorage,
        IValidator<SendMessageRequest> sendValidator,
        IValidator<UpdateMessageRequest> updateValidator,
        IValidator<AddReactionRequest> reactionValidator,
        ILogger<MessageService> logger)
    {
        _messageRepository = messageRepository;
        _userBlockService = userBlockService;
        _blobStorage = blobStorage;
        _sendValidator = sendValidator;
        _updateValidator = updateValidator;
        _reactionValidator = reactionValidator;
        _logger = logger;
    }

    // UC-4.1 Send Channel Message
    public async Task<MessageResponse> SendChannelMessageAsync(
        Guid channelId, string userId, SendMessageRequest request, CancellationToken ct = default)
    {
        await _sendValidator.ValidateAndThrowAsync(request, ct);

        var member = await _messageRepository.GetChannelMemberAsync(channelId, userId, ct)
            ?? throw new NotFoundException("Channel", channelId);

        var effectivePermissions = GetEffectivePermissions(member);
        if (!effectivePermissions.HasFlag(ChannelPermission.SendMessages))
            throw new BusinessRuleException("You do not have permission to send messages in this channel.");

        if (request.AttachmentIds is { Count: > 0 } &&
            !effectivePermissions.HasFlag(ChannelPermission.AttachFiles))
            throw new BusinessRuleException("You do not have permission to attach files in this channel.");

        var existing = await _messageRepository.GetByIdempotencyKeyAsync(userId, request.IdempotencyKey, ct);
        if (existing is not null)
        {
            _logger.LogWarning(
                "Idempotent short-circuit: message {MessageId} already exists for key {IdempotencyKey} by {UserId}",
                existing.MessageId, request.IdempotencyKey, userId);
            return await ToMessageResponseAsync(existing, userId, ct);
        }

        if (request.ParentMessageId.HasValue)
        {
            var parent = await _messageRepository.GetByIdAsync(request.ParentMessageId.Value, ct)
                ?? throw new NotFoundException("Message", request.ParentMessageId.Value);

            if (parent.ChannelId != channelId)
                throw new BusinessRuleException("Parent message does not belong to this channel.");
        }

        if (await _messageRepository.IsUserMutedAsync(channelId, userId, ct))
            throw new BusinessRuleException("You are muted in this channel.");

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            MessageId = Guid.NewGuid(),
            ChannelId = channelId,
            AuthorUserId = userId,
            Content = request.Content,
            IdempotencyKey = request.IdempotencyKey,
            SentAt = now,
            ParentMessageId = request.ParentMessageId,
            IsReply = request.ParentMessageId.HasValue
        };

        await ClaimAttachmentsForMessageAsync(userId, request.AttachmentIds, message.MessageId, ct);

        member.Channel.LastMessageAt = now;

        await _messageRepository.AddAsync(message, ct);

        try
        {
            await _messageRepository.SaveChangesAsync(ct);
        }
        catch (DuplicateException)
        {
            existing = await _messageRepository.GetByIdempotencyKeyAsync(userId, request.IdempotencyKey, ct)
                ?? throw new InvalidOperationException(
                    "Message was created concurrently but could not be retrieved.");

            _logger.LogWarning(
                "Concurrent idempotency resolved: message {MessageId} for key {IdempotencyKey} by {UserId}",
                existing.MessageId, request.IdempotencyKey, userId);

            return await ToMessageResponseAsync(existing, userId, ct);
        }

        _logger.LogInformation(
            "Channel message {MessageId} sent to channel {ChannelId} by {UserId}",
            message.MessageId, channelId, userId);

        var saved = await _messageRepository.GetByIdAsync(message.MessageId, ct);
        return await ToMessageResponseAsync(saved!, userId, ct);
    }

    // UC-4.2 Send Conversation Message
    public async Task<MessageResponse> SendConversationMessageAsync(
        Guid conversationId, string userId, SendMessageRequest request, CancellationToken ct = default)
    {
        await _sendValidator.ValidateAndThrowAsync(request, ct);

        var participant = await _messageRepository.GetConversationParticipantAsync(conversationId, userId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);

        var existing = await _messageRepository.GetByIdempotencyKeyAsync(userId, request.IdempotencyKey, ct);
        if (existing is not null)
        {
            _logger.LogWarning(
                "Idempotent short-circuit: message {MessageId} already exists for key {IdempotencyKey} by {UserId}",
                existing.MessageId, request.IdempotencyKey, userId);
            return await ToMessageResponseAsync(existing, userId, ct);
        }

        var messageId = Guid.NewGuid();
        await ClaimAttachmentsForMessageAsync(userId, request.AttachmentIds, messageId, ct);

        var otherParticipant = await GetOtherParticipantAsync(conversationId, userId, ct);
        await _userBlockService.EnsureNotBlockedAsync(userId, otherParticipant.UserId, ct);

        if (request.ParentMessageId.HasValue)
        {
            var parent = await _messageRepository.GetByIdAsync(request.ParentMessageId.Value, ct)
                ?? throw new NotFoundException("Message", request.ParentMessageId.Value);

            if (parent.ConversationId != conversationId)
                throw new BusinessRuleException("Parent message does not belong to this conversation.");
        }

        var now = DateTimeOffset.UtcNow;
        var message = new Message
        {
            MessageId = messageId,
            ConversationId = conversationId,
            AuthorUserId = userId,
            Content = request.Content,
            IdempotencyKey = request.IdempotencyKey,
            SentAt = now,
            ParentMessageId = request.ParentMessageId,
            IsReply = request.ParentMessageId.HasValue
        };

        if (otherParticipant.HiddenAt is not null)
            otherParticipant.HiddenAt = null;

        var conversation = await GetConversationAsync(conversationId, ct);
        conversation.LastMessageAt = now;

        await _messageRepository.AddAsync(message, ct);

        try
        {
            await _messageRepository.SaveChangesAsync(ct);
        }
        catch (DuplicateException)
        {
            existing = await _messageRepository.GetByIdempotencyKeyAsync(userId, request.IdempotencyKey, ct)
                ?? throw new InvalidOperationException(
                    "Message was created concurrently but could not be retrieved.");

            _logger.LogWarning(
                "Concurrent idempotency resolved: message {MessageId} for key {IdempotencyKey} by {UserId}",
                existing.MessageId, request.IdempotencyKey, userId);

            return await ToMessageResponseAsync(existing, userId, ct);
        }

        _logger.LogInformation(
            "Conversation message {MessageId} sent to conversation {ConversationId} by {UserId}",
            message.MessageId, conversationId, userId);

        var saved = await _messageRepository.GetByIdAsync(message.MessageId, ct);
        return await ToMessageResponseAsync(saved!, userId, ct);
    }

    // UC-4.3 Get Channel Messages
    public async Task<CursorPagedResponse<MessageResponse>> GetChannelMessagesAsync(
        Guid channelId, string userId, int? limit, string? cursor, CancellationToken ct = default)
    {
        var member = await _messageRepository.GetChannelMemberAsync(channelId, userId, ct)
            ?? throw new NotFoundException("Channel", channelId);

        var pageSize = NormalizeLimit(limit);
        DecodeCursor(cursor, out var cursorSentAt, out var cursorMessageId);

        // Fetch one extra to determine HasMore
        var messages = await _messageRepository.GetChannelMessagesAsync(
            channelId, pageSize + 1, cursorSentAt, cursorMessageId, ct);

        return await BuildPagedResponseAsync(messages, pageSize, userId, ct);
    }

    // UC-4.4 Get Conversation Messages
    public async Task<CursorPagedResponse<MessageResponse>> GetConversationMessagesAsync(
        Guid conversationId, string userId, int? limit, string? cursor, CancellationToken ct = default)
    {
        // 1. Participant check
        var participant = await _messageRepository.GetConversationParticipantAsync(conversationId, userId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);

        var pageSize = NormalizeLimit(limit);
        DecodeCursor(cursor, out var cursorSentAt, out var cursorMessageId);

        var messages = await _messageRepository.GetConversationMessagesAsync(
            conversationId, pageSize + 1, cursorSentAt, cursorMessageId, ct);

        return await BuildPagedResponseAsync(messages, pageSize, userId, ct);
    }

    // UC-4.5 Update Message
    public async Task<MessageResponse> UpdateMessageAsync(
        Guid messageId, string userId, UpdateMessageRequest request, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);

        var message = await _messageRepository.GetByIdAsync(messageId, ct)
            ?? throw new NotFoundException("Message", messageId);

        if (message.AuthorUserId != userId)
            throw new BusinessRuleException("Only the author can edit this message.");

        message.Content = request.Content;
        message.EditedAt = DateTimeOffset.UtcNow;

        await _messageRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Message {MessageId} updated by {UserId}", messageId, userId);

        return await ToMessageResponseAsync(message, userId, ct);
    }

    // UC-4.6 Delete Message
    public async Task DeleteMessageAsync(Guid messageId, string userId, CancellationToken ct = default)
    {
        var message = await _messageRepository.GetByIdAsync(messageId, ct)
            ?? throw new NotFoundException("Message", messageId);

        var isAuthor = message.AuthorUserId == userId;
        var isModerator = false;

        if (!isAuthor && message.ChannelId.HasValue)
        {
            var member = await _messageRepository.GetChannelMemberAsync(message.ChannelId.Value, userId, ct);
            if (member is not null)
            {
                var perms = GetEffectivePermissions(member);
                isModerator = perms.HasFlag(ChannelPermission.DeleteMessages);
            }
        }

        if (!isAuthor && !isModerator)
            throw new BusinessRuleException("You do not have permission to delete this message.");

        var blobPaths = message.Attachments
            .Select(a => a.StoragePath)
            .ToList();

        var replies = await _messageRepository.GetRepliesAsync(messageId, ct);
        foreach (var reply in replies)
            reply.ParentMessageId = null;

        await _messageRepository.DeleteAsync(message);

        if (isModerator && !isAuthor && message.ChannelId.HasValue)
        {
            var auditLog = new ChannelAuditLog
            {
                ChannelAuditLogId = Guid.NewGuid(),
                ChannelId = message.ChannelId.Value,
                ActorUserId = userId,
                Action = AuditAction.MessageDeleted,
                TargetType = "Message",
                TargetId = messageId.ToString(),
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _messageRepository.AddAuditLogAsync(auditLog, ct);
        }

        await _messageRepository.SaveChangesAsync(ct);

        foreach (var path in blobPaths)
        {
            try
            {
                await _blobStorage.DeleteAsync(path, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to delete blob {StoragePath} for message {MessageId}",
                    path, messageId);
            }
        }

        _logger.LogInformation("Message {MessageId} deleted by {UserId}", messageId, userId);
    }

    // UC-4.7 Add Reaction
    public async Task AddReactionAsync(
        Guid messageId, string userId, AddReactionRequest request, CancellationToken ct = default)
    {
        await _reactionValidator.ValidateAndThrowAsync(request, ct);

        var message = await _messageRepository.GetByIdAsync(messageId, ct)
            ?? throw new NotFoundException("Message", messageId);

        // Verify membership/participation
        if (message.ChannelId.HasValue)
        {
            var member = await _messageRepository.GetChannelMemberAsync(message.ChannelId.Value, userId, ct)
                ?? throw new NotFoundException("Channel", message.ChannelId.Value);

            var perms = GetEffectivePermissions(member);
            if (!perms.HasFlag(ChannelPermission.AddReactions))
                throw new BusinessRuleException("You do not have permission to add reactions in this channel.");
        }
        else if (message.ConversationId.HasValue)
        {
            _ = await _messageRepository.GetConversationParticipantAsync(message.ConversationId.Value, userId, ct)
                ?? throw new NotFoundException("Conversation", message.ConversationId.Value);
        }

        var reaction = new MessageReaction
        {
            MessageReactionId = Guid.NewGuid(),
            MessageId = messageId,
            ReactedByUserId = userId,
            Emoji = request.Emoji,
            ReactedAt = DateTimeOffset.UtcNow
        };

        await _messageRepository.AddReactionAsync(reaction, ct);
        await _messageRepository.SaveChangesAsync(ct); // Throws DuplicateException on unique violation

        _logger.LogInformation(
            "Reaction {Emoji} added to message {MessageId} by {UserId}",
            request.Emoji, messageId, userId);
    }

    // UC-4.8 Remove Reaction
    public async Task RemoveReactionAsync(
        Guid messageId, string userId, string emoji, CancellationToken ct = default)
    {
        var reaction = await _messageRepository.GetReactionAsync(messageId, userId, emoji, ct)
            ?? throw new NotFoundException("MessageReaction",
                $"Message={messageId}, User={userId}, Emoji={emoji}");

        await _messageRepository.DeleteReactionAsync(reaction);
        await _messageRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Reaction {Emoji} removed from message {MessageId} by {UserId}",
            emoji, messageId, userId);
    }

    // Helpers

    private static ChannelPermission GetEffectivePermissions(ChannelMember member)
    {
        var perms = member.Channel.DefaultPermissions;

        foreach (var assignedRole in member.AssignedRoles)
            perms |= assignedRole.Role.Permissions;

        // Owner implicitly has all permissions
        if (member.Channel.OwnerUserId == member.UserId)
            perms |= (ChannelPermission)~0L;

        if (perms.HasFlag(ChannelPermission.Administrator))
            perms |= (ChannelPermission)~0L;

        return perms;
    }

    private async Task ClaimAttachmentsForMessageAsync(
        string userId, IReadOnlyList<Guid>? attachmentIds, Guid messageId, CancellationToken ct)
    {
        if (attachmentIds is not { Count: > 0 })
            return;

        var attachments = await _messageRepository.GetAttachmentsByIdsAsync(attachmentIds, ct);

        foreach (var id in attachmentIds)
        {
            var attachment = attachments.FirstOrDefault(a => a.AttachmentId == id)
                ?? throw new NotFoundException("Attachment", id);

            if (attachment.UploadedByUserId != userId)
                throw new BusinessRuleException($"Attachment '{id}' does not belong to you.");

            if (attachment.Purpose != AttachmentPurpose.MessageAttachment)
                throw new BusinessRuleException($"Attachment '{id}' is not a message attachment.");

            if (attachment.MessageId is not null)
                throw new BusinessRuleException($"Attachment '{id}' is already claimed by another message.");
        }

        var claimed = await _messageRepository.ClaimAttachmentsAsync(attachmentIds, messageId, ct);
        if (claimed != attachmentIds.Count)
            throw new BusinessRuleException("One or more attachments were claimed by another request.");
    }

    private async Task<ConversationParticipant> GetOtherParticipantAsync(
        Guid conversationId, string userId, CancellationToken ct)
    {
        return await _messageRepository.GetOtherConversationParticipantAsync(conversationId, userId, ct)
            ?? throw new InvalidOperationException("Other participant could not be found.");
    }

    private async Task<Conversation> GetConversationAsync(Guid conversationId, CancellationToken ct)
    {
        return await _messageRepository.GetConversationAsync(conversationId, ct)
            ?? throw new NotFoundException("Conversation", conversationId);
    }

    private async Task<CursorPagedResponse<MessageResponse>> BuildPagedResponseAsync(
        IReadOnlyList<Message> messages, int pageSize, string userId, CancellationToken ct)
    {
        var hasMore = messages.Count > pageSize;
        var page = hasMore ? messages.Take(pageSize).ToList() : messages;

        string? nextCursor = null;
        if (hasMore)
        {
            var last = page[^1];
            nextCursor = EncodeCursor(last.SentAt, last.MessageId);
        }

        var items = new List<MessageResponse>(page.Count);
        foreach (var m in page)
            items.Add(await ToMessageResponseAsync(m, userId, ct));

        return new CursorPagedResponse<MessageResponse>
        {
            Items = items,
            HasMore = hasMore,
            NextCursor = nextCursor
        };
    }

    private async Task<MessageResponse> ToMessageResponseAsync(Message message, string userId, CancellationToken ct)
    {
        string? authorAvatarUrl = null;
        if (message.Author.Avatar is not null)
            authorAvatarUrl = await _blobStorage.GetDownloadUrlAsync(message.Author.Avatar.StoragePath, ct);

        var attachmentResponses = new List<AttachmentResponse>(message.Attachments.Count);
        foreach (var att in message.Attachments)
        {
            var url = await _blobStorage.GetDownloadUrlAsync(att.StoragePath, ct);
            attachmentResponses.Add(new AttachmentResponse
            {
                AttachmentId = att.AttachmentId,
                FileName = att.FileName,
                ContentType = att.ContentType,
                FileSizeBytes = att.FileSizeBytes,
                Url = url
            });
        }

        var reactionGroups = message.Reactions
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionGroup
            {
                Emoji = g.Key,
                Count = g.Count(),
                ReactedByMe = g.Any(r => r.ReactedByUserId == userId)
            })
            .ToList();

        ParentMessagePreview? parentPreview = null;
        if (message.ParentMessage is not null)
        {
            parentPreview = new ParentMessagePreview
            {
                MessageId = message.ParentMessage.MessageId,
                AuthorDisplayName = message.ParentMessage.Author.DisplayName,
                Content = message.ParentMessage.Content
            };
        }

        return new MessageResponse
        {
            MessageId = message.MessageId,
            Author = new UserSummary
            {
                Id = message.Author.UserId,
                DisplayName = message.Author.DisplayName,
                AvatarUrl = authorAvatarUrl
            },
            Content = message.Content,
            SentAt = message.SentAt,
            IsEdited = message.EditedAt.HasValue,
            IsReply = message.IsReply,
            ParentMessage = parentPreview,
            Attachments = attachmentResponses,
            Reactions = reactionGroups
        };
    }

    private static int NormalizeLimit(int? limit)
    {
        if (!limit.HasValue) return DefaultPageSize;
        return Math.Clamp(limit.Value, 1, MaxPageSize);
    }

    private static string EncodeCursor(DateTimeOffset sentAt, Guid messageId)
    {
        var raw = $"{sentAt.UtcTicks}{CursorSeparator}{messageId}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static void DecodeCursor(string? cursor, out DateTimeOffset? sentAt, out Guid? messageId)
    {
        sentAt = null;
        messageId = null;

        if (string.IsNullOrWhiteSpace(cursor))
            return;

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split(CursorSeparator, 2);
            if (parts.Length != 2)
                return;

            if (long.TryParse(parts[0], out var ticks) && Guid.TryParse(parts[1], out var id))
            {
                sentAt = new DateTimeOffset(ticks, TimeSpan.Zero);
                messageId = id;
            }
        }
        catch (FormatException)
        {
            // Invalid cursor — treat as no cursor (start from the beginning)
        }
    }
}
