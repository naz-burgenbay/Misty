using Misty.Core.Enums;

namespace Misty.Core.Data.Entities
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
        public byte[] RowVersion { get; set; } = null!;

        // Navigation Properties
        public Channel? Channel { get; set; }
        public ApplicationUser? TargetUser { get; set; }
        public ApplicationUser? CreatedBy { get; set; }
        public ApplicationUser? UpdatedBy { get; set; }
    }
}
