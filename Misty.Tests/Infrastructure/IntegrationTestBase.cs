using Microsoft.EntityFrameworkCore;
using Misty.Domain.Entities;
using Misty.Domain.Enums;
using Misty.Infrastructure;

namespace Misty.Tests.Infrastructure;

// Base class for integration tests.

[Collection(IntegrationTestCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly IntegrationTestFixture Fixture;
    protected ApplicationDbContext Db = null!;

    protected IntegrationTestBase(IntegrationTestFixture fixture)
    {
        Fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await Fixture.ResetAsync();
        Db = Fixture.CreateDbContext();
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
    }

    // Seed helpers

    protected async Task<User> SeedUserAsync(string? id = null, string? displayName = null)
    {
        var userId = id ?? $"user-{Guid.NewGuid():N}";
        var name = displayName ?? $"User-{userId[..8]}";
        var user = new User
        {
            UserId = userId,
            Username = name.ToLowerInvariant(),
            NormalizedUsername = name.ToUpperInvariant(),
            DisplayName = name,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Db.DomainUsers.Add(user);
        await Db.SaveChangesAsync();
        return user;
    }

    // Seed a channel with Owner + Moderator system roles and a member + role for the owner. Returns (channel, ownerMember, ownerRole, moderatorRole).
    protected async Task<(Channel Channel, ChannelMember OwnerMember, ChannelRole OwnerRole, ChannelRole ModeratorRole)>
        SeedChannelAsync(User owner, ChannelPermission? defaultPermissions = null)
    {
        var channel = new Channel
        {
            ChannelId = Guid.NewGuid(),
            Name = $"test-channel-{Guid.NewGuid():N[..8]}",
            OwnerUserId = owner.UserId,
            DefaultPermissions = defaultPermissions ??
                (ChannelPermission.SendMessages | ChannelPermission.AddReactions | ChannelPermission.AttachFiles),
            MemberCount = 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Db.Channels.Add(channel);

        var ownerRole = new ChannelRole
        {
            ChannelRoleId = Guid.NewGuid(),
            ChannelId = channel.ChannelId,
            Name = "Owner",
            Permissions = ChannelPermission.Administrator,
            Position = 100,
            IsSystemRole = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var modRole = new ChannelRole
        {
            ChannelRoleId = Guid.NewGuid(),
            ChannelId = channel.ChannelId,
            Name = "Moderator",
            Permissions = ChannelPermission.DeleteMessages | ChannelPermission.MuteUsers
                        | ChannelPermission.BanUsers | ChannelPermission.ViewAuditLog,
            Position = 50,
            IsSystemRole = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Db.ChannelRoles.Add(ownerRole);
        Db.ChannelRoles.Add(modRole);

        var member = new ChannelMember
        {
            ChannelMemberId = Guid.NewGuid(),
            ChannelId = channel.ChannelId,
            UserId = owner.UserId,
            JoinedAt = DateTimeOffset.UtcNow,
        };
        Db.ChannelMembers.Add(member);

        var memberRole = new ChannelMemberRole
        {
            ChannelMemberId = member.ChannelMemberId,
            ChannelRoleId = ownerRole.ChannelRoleId,
            AssignedAt = DateTimeOffset.UtcNow
        };
        Db.ChannelMemberRoles.Add(memberRole);

        await Db.SaveChangesAsync();
        return (channel, member, ownerRole, modRole);
    }

    protected async Task<ChannelMember> SeedMemberAsync(Channel channel, User user, ChannelRole? role = null)
    {
        var member = new ChannelMember
        {
            ChannelMemberId = Guid.NewGuid(),
            ChannelId = channel.ChannelId,
            UserId = user.UserId,
            JoinedAt = DateTimeOffset.UtcNow,
        };
        Db.ChannelMembers.Add(member);

        if (role is not null)
        {
            Db.ChannelMemberRoles.Add(new ChannelMemberRole
            {
                ChannelMemberId = member.ChannelMemberId,
                ChannelRoleId = role.ChannelRoleId,
                AssignedAt = DateTimeOffset.UtcNow
            });
        }

        channel.MemberCount++;
        await Db.SaveChangesAsync();
        return member;
    }

    protected async Task<Conversation> SeedConversationAsync(User userA, User userB)
    {
        var (low, high) = Conversation.NormalizeParticipants(userA.UserId, userB.UserId);
        var convo = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            ParticipantLowUserId = low,
            ParticipantHighUserId = high,
            CreatedAt = DateTimeOffset.UtcNow,
            Participants =
            {
                new ConversationParticipant
                {
                    ConversationParticipantId = Guid.NewGuid(),
                    UserId = userA.UserId,
                    JoinedAt = DateTimeOffset.UtcNow
                },
                new ConversationParticipant
                {
                    ConversationParticipantId = Guid.NewGuid(),
                    UserId = userB.UserId,
                    JoinedAt = DateTimeOffset.UtcNow
                }
            }
        };
        Db.Conversations.Add(convo);
        await Db.SaveChangesAsync();
        return convo;
    }

    protected async Task<Message> SeedChannelMessageAsync(
        Guid channelId, string authorUserId, string? content = null,
        DateTimeOffset? sentAt = null, string? idempotencyKey = null)
    {
        var msg = new Message
        {
            MessageId = Guid.NewGuid(),
            ChannelId = channelId,
            AuthorUserId = authorUserId,
            Content = content ?? "Test message",
            SentAt = sentAt ?? DateTimeOffset.UtcNow,
            IdempotencyKey = idempotencyKey
        };
        Db.Messages.Add(msg);
        await Db.SaveChangesAsync();
        return msg;
    }

    protected async Task<Message> SeedConversationMessageAsync(
        Guid conversationId, string authorUserId, string? content = null,
        DateTimeOffset? sentAt = null)
    {
        var msg = new Message
        {
            MessageId = Guid.NewGuid(),
            ConversationId = conversationId,
            AuthorUserId = authorUserId,
            Content = content ?? "Test message",
            SentAt = sentAt ?? DateTimeOffset.UtcNow,
        };
        Db.Messages.Add(msg);
        await Db.SaveChangesAsync();
        return msg;
    }

    // Detach all tracked entities to avoid reading cached state
    protected void DetachAll()
    {
        foreach (var entry in Db.ChangeTracker.Entries().ToList())
            entry.State = EntityState.Detached;
    }
}
