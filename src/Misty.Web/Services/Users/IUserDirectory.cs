using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Misty.Web.Services.Users;

public sealed record UserSummary(Guid Id, string DisplayName, string Username, bool IsAi = false);

// Per-session cache of user identity for message rendering. Misses trigger a one-shot fetch and an Updated event so subscribers can re-render once the real name arrives. Failures fall back to a stable placeholder so the UI never shows a partially-loaded user.
public interface IUserDirectory
{
    UserSummary Get(Guid id);
    Task EnsureAsync(Guid id, CancellationToken ct = default);
    void Seed(UserSummary user);
    event Action<Guid>? Updated;
}

public sealed class HttpUserDirectory : IUserDirectory
{
    private readonly HttpClient _http;
    private readonly ILogger<HttpUserDirectory> _logger;
    private readonly Dictionary<Guid, UserSummary> _cache = new();
    private readonly HashSet<Guid> _inFlight = new();

    public event Action<Guid>? Updated;

    public HttpUserDirectory(HttpClient http, ILogger<HttpUserDirectory> logger)
    {
        _http = http;
        _logger = logger;
    }

    public UserSummary Get(Guid id)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(id, out var u)) return u;
        }
        // Render a placeholder; caller should also schedule an EnsureAsync to populate the cache.
        var shortId = id.ToString("N")[..6];
        return new UserSummary(id, $"User {shortId}", shortId);
    }

    public void Seed(UserSummary user)
    {
        bool changed;
        lock (_cache)
        {
            changed = !_cache.TryGetValue(user.Id, out var existing) || existing != user;
            _cache[user.Id] = user;
        }
        if (changed) Updated?.Invoke(user.Id);
    }

    public async Task EnsureAsync(Guid id, CancellationToken ct = default)
    {
        lock (_cache)
        {
            if (_cache.ContainsKey(id) || !_inFlight.Add(id)) return;
        }

        try
        {
            var resp = await _http.GetAsync($"api/v1/users/{id}", ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return;
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<UserByIdDto>(cancellationToken: ct);
            if (body is null) return;
            Seed(new UserSummary(body.UserId, body.DisplayName, body.Username));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch user {UserId}.", id);
        }
        finally
        {
            lock (_cache) { _inFlight.Remove(id); }
        }
    }

    private sealed record UserByIdDto(Guid UserId, string Username, string DisplayName, string? Bio, string? AvatarUrl, string Version);
}
