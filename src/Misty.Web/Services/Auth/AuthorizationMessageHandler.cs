using System.Net;
using System.Net.Http.Headers;

namespace Misty.Web.Services.Auth;

// Attaches the bearer access token to outgoing requests and, on a 401, refreshes once and retries. The refresh itself is serialized by a SemaphoreSlim inside HttpAuthService, so a 401 storm produces a single network refresh.
public sealed class AuthorizationMessageHandler : DelegatingHandler
{
    private readonly IAuthService _auth;

    public AuthorizationMessageHandler(IAuthService auth) => _auth = auth;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        await AttachTokenAsync(request, ct);
        var response = await base.SendAsync(request, ct);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        // One retry after a forced refresh. HttpAuthService.GetAccessTokenAsync(forceRefresh: true) is the single source of refresh serialization.
        if (_auth is HttpAuthService http)
        {
            var refreshed = await http.GetAccessTokenAsync(forceRefresh: true, ct);
            if (string.IsNullOrEmpty(refreshed))
                return response;

            response.Dispose();
            var retry = await CloneAsync(request, ct);
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", refreshed);
            return await base.SendAsync(retry, ct);
        }

        return response;
    }

    private async Task AttachTokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _auth.GetAccessTokenAsync(ct);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage source, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri) { Version = source.Version };
        foreach (var (k, v) in source.Headers)
            clone.Headers.TryAddWithoutValidation(k, v);

        if (source.Content is not null)
        {
            var buffer = await source.Content.ReadAsByteArrayAsync(ct);
            clone.Content = new ByteArrayContent(buffer);
            foreach (var (k, v) in source.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(k, v);
        }

        return clone;
    }
}
