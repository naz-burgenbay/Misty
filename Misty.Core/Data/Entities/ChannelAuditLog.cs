using Misty.Application.Enums;

namespace Misty.Core.Data.Entities
{
    public class ChannelAuditLog
    {
        public Guid ChannelAuditLogId { get; set; }
        public Guid ChannelId { get; set; }
        public required string ActorUserId { get; set; }
        public AuditAction Action { get; set; }
        public string? TargetType { get; set; }
        public string? TargetId { get; set; }
        public string? Details { get; set; }
        public string? ActorDisplayName { get; set; }
        public string? IpAddress { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        // Navigation Properties
        public Channel Channel { get; set; } = null!;
        public ApplicationUser? Actor { get; set; }
    }
}
