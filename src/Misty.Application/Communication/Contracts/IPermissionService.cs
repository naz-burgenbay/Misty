using Misty.Domain.Communication;

namespace Misty.Application.Communication.Contracts;

public interface IPermissionService
{
    Task<bool> CheckPermissionAsync(
        Guid userId,
        Guid channelId,
        ChannelPermission permission,
        CancellationToken ct = default);

    // Returns the effective permission bitmask for the user on the channel. Banned users and non-members get ChannelPermission.None; muted users get write-class bits stripped.
    Task<ChannelPermission> GetEffectivePermissionsAsync(
        Guid userId,
        Guid channelId,
        CancellationToken ct = default);
}
