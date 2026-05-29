using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Misty.Web.Services.Common;
using Misty.Web.Services.Realtime;

namespace Misty.Web.Services.Permissions;

// Mirrors Misty.Domain.Communication.ChannelPermission bit-for-bit so the long returned by the API can be cast directly into this enum on the client.
[Flags]
public enum ChannelPermissionFlags : long
{
    None             = 0,

    ViewChannel      = 1L << 0,
    ReadHistory      = 1L << 1,

    SendMessages     = 1L << 2,
    AttachFiles      = 1L << 3,
    AddReactions     = 1L << 4,
    MentionEveryone  = 1L << 5,

    ManageMessages   = 1L << 6,
    MuteMembers      = 1L << 7,
    BanMembers       = 1L << 8,
    KickMembers      = 1L << 9,

    ManageChannel    = 1L << 10,
    ManageRoles      = 1L << 11,
    ManageMembers    = 1L << 12,
}

public static class ChannelPermissionFlagsExtensions
{
    private const ChannelPermissionFlags ModerationMask =
        ChannelPermissionFlags.MuteMembers
        | ChannelPermissionFlags.BanMembers
        | ChannelPermissionFlags.KickMembers
        | ChannelPermissionFlags.ManageMessages;

    public static bool CanModerate(this ChannelPermissionFlags flags) =>
        (flags & ModerationMask) != 0;
}

// Per-channel permission cache invalidated on MembershipChanged/RoleChanged/ModerationActionApplied SignalR events broadcast by PermissionEventsBroadcastWorker.
// Returns the current user's effective flags for a given channel.
public interface IPermissionsCache
{
    ChannelPermissionFlags Get(Guid channelId);
    Observable<ChannelPermissionFlags> Watch(Guid channelId);
    bool Has(Guid channelId, ChannelPermissionFlags flag);
    void Invalidate(Guid channelId);
}

public sealed class HttpPermissionsCache : IPermissionsCache, IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpPermissionsCache> _logger;
    private readonly Dictionary<Guid, Observable<ChannelPermissionFlags>> _byChannel = new();
    private readonly HashSet<Guid> _inFlight = new();
    private readonly object _gate = new();

    private readonly List<IDisposable> _hubSubs = new();

    public HttpPermissionsCache(HttpClient http, ISignalRClient hub, ILogger<HttpPermissionsCache> logger)
    {
        _http = http;
        _logger = logger;

        _hubSubs.Add(hub.OnMembershipChanged(e => Invalidate(e.ChannelId)));
        _hubSubs.Add(hub.OnRoleChanged(e => Invalidate(e.ChannelId)));
        _hubSubs.Add(hub.OnModerationActionApplied(e => Invalidate(e.ChannelId)));
    }

    public ChannelPermissionFlags Get(Guid channelId) => Watch(channelId).Value;

    public Observable<ChannelPermissionFlags> Watch(Guid channelId)
    {
        Observable<ChannelPermissionFlags> obs;
        bool fetch;
        lock (_gate)
        {
            if (!_byChannel.TryGetValue(channelId, out obs!))
            {
                obs = new Observable<ChannelPermissionFlags>(ChannelPermissionFlags.None);
                _byChannel[channelId] = obs;
                fetch = _inFlight.Add(channelId);
            }
            else fetch = false;
        }
        if (fetch) _ = RefreshAsync(channelId);
        return obs;
    }

    public bool Has(Guid channelId, ChannelPermissionFlags flag) => (Get(channelId) & flag) == flag;

    public void Invalidate(Guid channelId)
    {
        lock (_gate)
        {
            if (!_byChannel.ContainsKey(channelId)) return;
            if (!_inFlight.Add(channelId)) return;
        }
        _ = RefreshAsync(channelId);
    }

    private async Task RefreshAsync(Guid channelId)
    {
        try
        {
            var body = await _http.GetFromJsonAsync<PermissionsResponse>(
                $"api/v1/channels/{channelId}/permissions/me");
            if (body is null) return;

            Observable<ChannelPermissionFlags>? obs;
            lock (_gate) { _byChannel.TryGetValue(channelId, out obs); }
            obs?.Set((ChannelPermissionFlags)body.EffectivePermissions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch permissions for channel {ChannelId}.", channelId);
        }
        finally
        {
            lock (_gate) { _inFlight.Remove(channelId); }
        }
    }

    public void Dispose()
    {
        foreach (var sub in _hubSubs) sub.Dispose();
        _hubSubs.Clear();
    }

    private sealed record PermissionsResponse(Guid ChannelId, long EffectivePermissions);
}
