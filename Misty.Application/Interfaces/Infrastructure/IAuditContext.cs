namespace Misty.Application.Interfaces;

public interface IAuditContext
{
    string? IpAddress { get; }
    string? ActorDisplayName { get; }
}
