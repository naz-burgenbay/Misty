using Misty.Domain.Communication;

namespace Misty.Application.Communication.Contracts;

public sealed record ChannelSummary(
    Guid Id,
    string Name,
    bool IsPrivate,
    bool IsAiAssistantEnabled,
    ChannelPermission DefaultPermissions);

// Cross-module query service that allows non-communication modules (e.g. Messaging) to look up basic channel data without depending directly on the Communication infrastructure.
public interface IChannelQueryService
{
    Task<ChannelSummary?> GetByIdAsync(Guid channelId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid channelId, CancellationToken ct = default);
}
