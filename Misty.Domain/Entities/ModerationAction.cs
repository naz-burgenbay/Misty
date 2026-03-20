using Misty.Domain.Enums;

namespace Misty.Domain.Entities
{
    public class ModerationAction
    {
        public Guid ModerationActionId { get; set; }
        public Guid ChannelId { get; set; }
        public required string TargetUserId { get; set; }
        public required string CreatedByUserId { get; set; }
        public ModerationType Type { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTimeOffset StartAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? UpdatedByUserId { get; set; }
        public string? TargetUserDisplayName { get; set; }
        public string? CreatedByDisplayName { get; set; }
        public string? UpdatedByDisplayName { get; set; }
        public byte[] Version { get; set; } = null!;

        // Navigation Properties
        public Channel? Channel { get; set; }
        public User? TargetUser { get; set; }
        public User? CreatedBy { get; set; }
        public User? UpdatedBy { get; set; }
    }
}
