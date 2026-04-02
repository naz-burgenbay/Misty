using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Tests.Common;

/// <summary>
/// Fluent builder for creating domain entities with realistic defaults.
/// Keeps tests focused on "what varies" instead of boilerplate construction.
/// </summary>
public static class TestData
{
    private static int _counter;
    private static string NextId() => $"user-{Interlocked.Increment(ref _counter)}";

    public static User User(string? id = null, string? displayName = null) => new()
    {
        UserId = id ?? NextId(),
        Username = displayName?.ToLowerInvariant() ?? $"user{_counter}",
        NormalizedUsername = displayName?.ToUpperInvariant() ?? $"USER{_counter}",
        DisplayName = displayName ?? $"User {_counter}",
        CreatedAt = DateTimeOffset.UtcNow,
        Version = [0, 0, 0, 1]
    };

    public static Channel Channel(string ownerUserId, ChannelPermission? defaultPermissions = null) => new()
    {
        ChannelId = Guid.NewGuid(),
        Name = $"Channel {Interlocked.Increment(ref _counter)}",
        OwnerUserId = ownerUserId,
        CreatedAt = DateTimeOffset.UtcNow,
        DefaultPermissions = defaultPermissions ?? (ChannelPermission.SendMessages | ChannelPermission.AddReactions | ChannelPermission.AttachFiles),
        MemberCount = 1,
        Version = [0, 0, 0, 1]
    };

    public static ChannelMember Member(Channel channel, User user) => new()
    {
        ChannelMemberId = Guid.NewGuid(),
        ChannelId = channel.ChannelId,
        Channel = channel,
        UserId = user.UserId,
        User = user,
        JoinedAt = DateTimeOffset.UtcNow,
        AssignedRoles = new List<ChannelMemberRole>()
    };

    public static ChannelRole Role(Channel channel, string name, ChannelPermission permissions, int position, bool isSystem = false) => new()
    {
        ChannelRoleId = Guid.NewGuid(),
        ChannelId = channel.ChannelId,
        Channel = channel,
        Name = name,
        Permissions = permissions,
        Position = position,
        IsSystemRole = isSystem,
        CreatedAt = DateTimeOffset.UtcNow,
        Version = [0, 0, 0, 1]
    };

    public static void AssignRole(ChannelMember member, ChannelRole role)
    {
        member.AssignedRoles.Add(new ChannelMemberRole
        {
            ChannelMemberId = member.ChannelMemberId,
            ChannelRoleId = role.ChannelRoleId,
            Member = member,
            Role = role,
            AssignedAt = DateTimeOffset.UtcNow
        });
    }

    public static ModerationAction Moderation(
        Channel channel, string targetUserId, string createdByUserId,
        ModerationType type, bool isActive = true, DateTimeOffset? expiresAt = null) => new()
    {
        ModerationActionId = Guid.NewGuid(),
        ChannelId = channel.ChannelId,
        Channel = channel,
        TargetUserId = targetUserId,
        CreatedByUserId = createdByUserId,
        Type = type,
        Reason = "Test reason",
        StartAt = DateTimeOffset.UtcNow,
        ExpiresAt = expiresAt,
        IsActive = isActive,
        Version = [0, 0, 0, 1]
    };

    public static UserBlock Block(string blockingUserId, string blockedUserId) => new()
    {
        UserBlockId = Guid.NewGuid(),
        BlockingUserId = blockingUserId,
        BlockedUserId = blockedUserId,
        BlockedAt = DateTimeOffset.UtcNow
    };

    public static Conversation Conversation(string userIdA, string userIdB)
    {
        var (low, high) = Domain.Entities.Conversation.NormalizeParticipants(userIdA, userIdB);
        return new Conversation
        {
            ConversationId = Guid.NewGuid(),
            ParticipantLowUserId = low,
            ParticipantHighUserId = high,
            CreatedAt = DateTimeOffset.UtcNow,
            Participants = new List<ConversationParticipant>
            {
                new()
                {
                    ConversationParticipantId = Guid.NewGuid(),
                    UserId = userIdA,
                    JoinedAt = DateTimeOffset.UtcNow
                },
                new()
                {
                    ConversationParticipantId = Guid.NewGuid(),
                    UserId = userIdB,
                    JoinedAt = DateTimeOffset.UtcNow
                }
            }
        };
    }

    public static Message ChannelMessage(Guid channelId, string authorUserId) => new()
    {
        MessageId = Guid.NewGuid(),
        ChannelId = channelId,
        AuthorUserId = authorUserId,
        Content = "Test message",
        SentAt = DateTimeOffset.UtcNow
    };

    public static Message ConversationMessage(Guid conversationId, string authorUserId) => new()
    {
        MessageId = Guid.NewGuid(),
        ConversationId = conversationId,
        AuthorUserId = authorUserId,
        Content = "Test message",
        SentAt = DateTimeOffset.UtcNow
    };
}
