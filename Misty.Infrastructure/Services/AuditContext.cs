using Microsoft.AspNetCore.Http;
using Misty.Application.Interfaces;

namespace Misty.Infrastructure.Services;

public class AuditContext : IAuditContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? ActorDisplayName => _httpContextAccessor.HttpContext?.User.FindFirst("display_name")?.Value;
}
