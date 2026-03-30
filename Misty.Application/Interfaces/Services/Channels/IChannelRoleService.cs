using Misty.Application.DTOs.Channels;

namespace Misty.Application.Interfaces.Channels;

public interface IChannelRoleService
{
    Task<IReadOnlyList<ChannelRoleResponse>> GetRolesAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task<ChannelRoleResponse> GetRoleAsync(Guid channelId, Guid roleId, string userId, CancellationToken ct = default);
    Task<ChannelRoleResponse> CreateRoleAsync(Guid channelId, string userId, CreateChannelRoleRequest request, CancellationToken ct = default);
    Task<ChannelRoleResponse> UpdateRoleAsync(Guid channelId, Guid roleId, string userId, UpdateChannelRoleRequest request, CancellationToken ct = default);
    Task DeleteRoleAsync(Guid channelId, Guid roleId, string userId, CancellationToken ct = default);
}
