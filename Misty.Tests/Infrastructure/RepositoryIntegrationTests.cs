using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Misty.Application.Exceptions;
using Misty.Domain.Entities;
using Misty.Domain.Enums;
using Misty.Infrastructure.Data.Repositories;

namespace Misty.Tests.Infrastructure;

public class RepositoryIntegrationTests : IntegrationTestBase
{
    public RepositoryIntegrationTests(IntegrationTestFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateConversation_DuplicatePair_ThrowsDuplicateException()
    {
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync();
        await SeedConversationAsync(userA, userB);
        DetachAll();

        var repo = new ConversationRepository(Db);
        var (low, high) = Conversation.NormalizeParticipants(userA.UserId, userB.UserId);

        var duplicate = new Conversation
        {
            ConversationId = Guid.NewGuid(),
            ParticipantLowUserId = low,
            ParticipantHighUserId = high,
            CreatedAt = DateTimeOffset.UtcNow,
            Participants =
            {
                new ConversationParticipant { UserId = userA.UserId, JoinedAt = DateTimeOffset.UtcNow },
                new ConversationParticipant { UserId = userB.UserId, JoinedAt = DateTimeOffset.UtcNow }
            }
        };

        var act = () => repo.CreateConversationAsync(duplicate);

        await act.Should().ThrowAsync<DuplicateException>()
            .WithMessage("*Conversation*");
    }

    [Fact]
    public async Task SaveMessage_BothChannelAndConversation_ThrowsDbUpdateException()
    {
        var user = await SeedUserAsync();
        var (channel, _, _, _) = await SeedChannelAsync(user);
        var convo = await SeedConversationAsync(user, await SeedUserAsync());

        var msg = new Message
        {
            MessageId = Guid.NewGuid(),
            ChannelId = channel.ChannelId,
            ConversationId = convo.ConversationId,
            AuthorUserId = user.UserId,
            Content = "violates XOR",
            SentAt = DateTimeOffset.UtcNow
        };
        Db.Messages.Add(msg);

        var act = () => Db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>("CK_Message_Target forbids both targets");
    }

    [Fact]
    public async Task GetChannelMessages_CursorPagination_ReturnsDescendingWithTiebreaker()
    {
        var user = await SeedUserAsync();
        var (channel, _, _, _) = await SeedChannelAsync(user);

        var sameTime = DateTimeOffset.UtcNow;
        var msg1 = await SeedChannelMessageAsync(channel.ChannelId, user.UserId, "first", sameTime);
        var msg2 = await SeedChannelMessageAsync(channel.ChannelId, user.UserId, "second", sameTime);
        var msg3 = await SeedChannelMessageAsync(channel.ChannelId, user.UserId, "third", sameTime.AddSeconds(1));
        DetachAll();

        var repo = new MessageRepository(Db);

        var page1 = await repo.GetChannelMessagesAsync(channel.ChannelId, 2, null, null);
        page1.Should().HaveCount(2);
        page1[0].MessageId.Should().Be(msg3.MessageId, "newest message first");

        var cursor = page1[^1];
        var page2 = await repo.GetChannelMessagesAsync(channel.ChannelId, 2, cursor.SentAt, cursor.MessageId);
        page2.Should().HaveCountGreaterThanOrEqualTo(1);
        page2.Should().NotContain(m => page1.Select(p => p.MessageId).Contains(m.MessageId),
            "cursor must exclude previously returned items");
    }

    [Fact]
    public async Task SaveChanges_StaleRowVersion_ThrowsConcurrencyException()
    {
        var user = await SeedUserAsync();
        var (channel, _, _, _) = await SeedChannelAsync(user);
        DetachAll();

        await using var db1 = Fixture.CreateDbContext();
        await using var db2 = Fixture.CreateDbContext();

        var chan1 = await db1.Channels.FirstAsync(c => c.ChannelId == channel.ChannelId);
        var chan2 = await db2.Channels.FirstAsync(c => c.ChannelId == channel.ChannelId);

        // Writer 1 wins
        chan1.Name = "Updated by writer 1";
        await db1.SaveChangesAsync();

        // Writer 2 has stale version
        chan2.Name = "Updated by writer 2";

        var repo2 = new ChannelRepository(db2);
        var act = () => repo2.SaveChangesAsync();

        await act.Should().ThrowAsync<ConcurrencyException>("stale RowVersion must trigger conflict");
    }
}
