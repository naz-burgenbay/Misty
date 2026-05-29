using System.Net.Http.Json;

namespace Misty.Web.Services.Communication;

public enum ModerationActionKind
{
    Mute = 0,
    Ban = 1,
    Warn = 2,
}

public interface IModerationService
{
    Task<Guid> ApplyAsync(Guid channelId, Guid userId, ModerationActionKind kind, string reason,
        DateTime? expiresAt = null, CancellationToken ct = default);
}

public sealed class HttpModerationService : IModerationService
{
    private readonly HttpClient _http;

    public HttpModerationService(HttpClient http) => _http = http;

    public async Task<Guid> ApplyAsync(Guid channelId, Guid userId, ModerationActionKind kind, string reason,
        DateTime? expiresAt = null, CancellationToken ct = default)
    {
        var body = new ApplyRequestDto((int)kind, reason, expiresAt);
        using var resp = await _http.PostAsJsonAsync(
            $"api/v1/channels/{channelId}/members/{userId}/moderation", body, ct);
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<ApplyResponseDto>(cancellationToken: ct)
                      ?? throw new InvalidOperationException("Empty moderation response.");
        return payload.ActionId;
    }

    private sealed record ApplyRequestDto(int Type, string Reason, DateTime? ExpiresAt);
    private sealed record ApplyResponseDto(Guid ActionId);
}
