using Misty.Application.Communication.Contracts;
using Misty.Domain.Communication;
using StackExchange.Redis;

namespace Misty.Infrastructure.Communication;

// Redis cache layer over PermissionService. Cache misses fall back to SQL and populate the cache automatically.
public sealed class CachedPermissionService : IPermissionService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private readonly PermissionService _inner;
    private readonly IDatabase _redis;

    public CachedPermissionService(PermissionService inner, IConnectionMultiplexer mux)
    {
        _inner = inner;
        _redis = mux.GetDatabase();
    }

    public async Task<bool> CheckPermissionAsync(
        Guid userId,
        Guid channelId,
        ChannelPermission permission,
        CancellationToken ct = default)
    {
        var key = CacheKey(userId, channelId);
        var cached = await _redis.StringGetAsync(key);

        long effective;
        if (cached.HasValue)
        {
            effective = (long)cached;
        }
        else
        {
            effective = await _inner.ComputeEffectivePermissionsAsync(userId, channelId, ct);
            await _redis.StringSetAsync(key, effective, Ttl);
        }

        if (effective == PermissionService.DeniedSentinel)
            return false;

        return ((ChannelPermission)effective & permission) == permission;
    }

    public static string CacheKey(Guid userId, Guid channelId) =>
        $"perm:{userId}:{channelId}";
}
