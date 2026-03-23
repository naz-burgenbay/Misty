using Misty.Domain.Enums;

namespace Misty.Application.DTOs.Channels;

public record ChannelAuditLogResponse
{
    public Guid ChannelAuditLogId { get; init; }
    public AuditAction Action { get; init; }
    public string? ActorDisplayName { get; init; }
    public string? TargetType { get; init; }
    public string? TargetId { get; init; }
    public string? Details { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
