namespace Misty.Core.Data.Entities
{
    public class Channel
    {
        public Guid ChannelId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? IconUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public bool IsPrivate { get; set; }
        public string? InviteCode { get; set; }
        public bool IsAiAssistantEnabled { get; set; }
        public required string CreatedByUserId { get; set; }

        // Navigation Properties
        public ApplicationUser Creator { get; set; } = null!;
        public ICollection<ChannelMember> Members { get; set; } = new List<ChannelMember>();
        public ICollection<ChannelRole> Roles { get; set; } = new List<ChannelRole>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<ModerationAction> ModerationActions { get; set; } = new List<ModerationAction>();
    }
}
