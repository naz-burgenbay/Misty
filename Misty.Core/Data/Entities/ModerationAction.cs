using Misty.Core.Enums;

namespace Misty.Core.Data.Entities
{
    public class ModerationAction
    {
        public Guid ModerationActionId { get; set; }
        public Guid? ChannelId { get; set; }
        public string? TargetUserId { get; set; }
        public string? CreatedByUserId { get; set; }
        public ModerationType Type { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime StartAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedByUserId { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        // Snapshot fields
        public string? TargetUserDisplayName { get; set; }
        public string? CreatedByDisplayName { get; set; }
        public string? UpdatedByDisplayName { get; set; }

        // Navigation Properties
        public Channel? Channel { get; set; }
        public ApplicationUser? TargetUser { get; set; }
        public ApplicationUser? CreatedBy { get; set; }
        public ApplicationUser? UpdatedBy { get; set; }
    }
}
