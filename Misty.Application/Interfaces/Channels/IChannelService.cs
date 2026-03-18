using Misty.Application.DTOs;
using Misty.Application.DTOs.Channels;

namespace Misty.Application.Interfaces.Channels;

public interface IChannelService
{
    Task<ChannelDetailResponse> CreateChannelAsync(string userId, CreateChannelRequest request, CancellationToken ct = default);
    Task<ChannelDetailResponse> GetChannelAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task<IReadOnlyList<ChannelSummary>> GetUserChannelsAsync(string userId, CancellationToken ct = default);
    Task<ChannelDetailResponse> UpdateChannelAsync(Guid channelId, string userId, UpdateChannelRequest request, CancellationToken ct = default);
    Task DeleteChannelAsync(Guid channelId, string userId, CancellationToken ct = default);

    Task<ChannelDetailResponse> JoinByInviteCodeAsync(string inviteCode, string userId, CancellationToken ct = default);
    Task<string> GenerateInviteCodeAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task RevokeInviteCodeAsync(Guid channelId, string userId, CancellationToken ct = default);

    Task TransferOwnershipAsync(Guid channelId, string userId, TransferOwnershipRequest request, CancellationToken ct = default);

    Task SetChannelIconAsync(Guid channelId, string userId, Guid attachmentId, CancellationToken ct = default);
    Task RemoveChannelIconAsync(Guid channelId, string userId, CancellationToken ct = default);
}
