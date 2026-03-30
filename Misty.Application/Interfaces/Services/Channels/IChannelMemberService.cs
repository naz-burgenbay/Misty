using Misty.Application.DTOs.Channels;
using Misty.Application.DTOs.Common;

namespace Misty.Application.Interfaces.Channels;

public interface IChannelMemberService
{
    Task<PagedResponse<ChannelMemberResponse>> GetMembersAsync(Guid channelId, string userId, int page, int pageSize, CancellationToken ct = default);
    Task<ChannelMemberResponse> GetMemberAsync(Guid channelId, Guid memberId, string userId, CancellationToken ct = default);
    Task LeaveChannelAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task RemoveMemberAsync(Guid channelId, Guid memberId, string userId, CancellationToken ct = default);
    Task MarkChannelReadAsync(Guid channelId, string userId, MarkChannelReadRequest request, CancellationToken ct = default);
    Task<ChannelMemberResponse> UpdateMemberRolesAsync(Guid channelId, Guid memberId, string userId, UpdateChannelMemberRolesRequest request, CancellationToken ct = default);
}
