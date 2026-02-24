using Microsoft.AspNetCore.Identity;

namespace Misty.Core.Data.Entities
{
    public class ApplicationUser : IdentityUser
    {
        public string DisplayName { get; set; } = string.Empty;
        public string? Bio { get; set; }
        public string? AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? DeletedAt { get; set; }
        public bool IsActive { get; set; } = true;

        // Navigation Properties
        public ICollection<Channel> CreatedChannels { get; set; } = new List<Channel>();
        public ICollection<ChannelMember> Memberships { get; set; } = new List<ChannelMember>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
        public ICollection<ConversationParticipant> ConversationParticipants { get; set; } = new List<ConversationParticipant>();
        public ICollection<UserBlock> InitiatedBlocks { get; set; } = new List<UserBlock>();
        public ICollection<UserBlock> ReceivedBlocks { get; set; } = new List<UserBlock>();
        public ICollection<ModerationAction> TargetedModerationActions { get; set; } = new List<ModerationAction>();
        public ICollection<ModerationAction> CreatedModerationActions { get; set; } = new List<ModerationAction>();
        public ICollection<ModerationAction> UpdatedModerationActions { get; set; } = new List<ModerationAction>();
    }
}