using Misty.Domain.Enums;

namespace Misty.Application.Interfaces.Channels;

public interface IChannelPermissionService
{
    Task<ChannelPermission> GetEffectivePermissionsAsync(Guid channelId, string userId, CancellationToken ct = default);
    Task EnsurePermissionAsync(Guid channelId, string userId, ChannelPermission required, CancellationToken ct = default);
}
