using Microsoft.EntityFrameworkCore;
using Misty.Application.Communication.Contracts;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Communication;

public sealed class ChannelQueryService : IChannelQueryService
{
    private readonly ApplicationDbContext _db;

    public ChannelQueryService(ApplicationDbContext db) => _db = db;

    public async Task<ChannelSummary?> GetByIdAsync(Guid channelId, CancellationToken ct = default)
    {
        var channel = await _db.Channels
            .FirstOrDefaultAsync(c => c.Id == channelId && !c.IsDeleted, ct);

        return channel is null
            ? null
            : new ChannelSummary(
                channel.Id,
                channel.Name,
                channel.IsPrivate,
                channel.IsAiAssistantEnabled,
                channel.DefaultPermissions);
    }

    public Task<bool> ExistsAsync(Guid channelId, CancellationToken ct = default)
        => _db.Channels.AnyAsync(c => c.Id == channelId && !c.IsDeleted, ct);
}
