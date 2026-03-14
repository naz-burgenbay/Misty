using Microsoft.AspNetCore.Identity;

namespace Misty.Core.Data.Entities
{
    public class ApplicationUser : IdentityUser
    {
        [PersonalData]
        public string DisplayName { get; set; } = string.Empty;
        [PersonalData]
        public string? Bio { get; set; }
        [PersonalData]
        public Guid? AvatarAttachmentId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }

        // Navigation Properties
        public Attachment? Avatar { get; set; }
        public ICollection<Attachment> UploadedAttachments { get; set; } = new List<Attachment>();
        public ICollection<Channel> CreatedChannels { get; set; } = new List<Channel>();
        public ICollection<Channel> OwnedChannels { get; set; } = new List<Channel>();
        public ICollection<ChannelMember> Memberships { get; set; } = new List<ChannelMember>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
        public ICollection<ConversationParticipant> ConversationParticipants { get; set; } = new List<ConversationParticipant>();
        public ICollection<UserBlock> InitiatedBlocks { get; set; } = new List<UserBlock>();
        public ICollection<UserBlock> ReceivedBlocks { get; set; } = new List<UserBlock>();
        public ICollection<ModerationAction> TargetedModerationActions { get; set; } = new List<ModerationAction>();
        public ICollection<ModerationAction> CreatedModerationActions { get; set; } = new List<ModerationAction>();
        public ICollection<ModerationAction> UpdatedModerationActions { get; set; } = new List<ModerationAction>();
        public ICollection<ChannelAuditLog> AuditLogEntries { get; set; } = new List<ChannelAuditLog>();
    }
}