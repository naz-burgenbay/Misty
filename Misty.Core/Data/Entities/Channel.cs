using Misty.Application.Enums;

namespace Misty.Core.Data.Entities
{
    public class Channel
    {
        public Guid ChannelId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid? IconAttachmentId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
        public bool IsPrivate { get; set; }
        public string? InviteCode { get; set; }
        public bool IsAiAssistantEnabled { get; set; }
        public required string CreatedByUserId { get; set; }
        public required string OwnerUserId { get; set; }
        public ChannelPermission DefaultPermissions { get; set; } = ChannelPermission.SendMessages | ChannelPermission.AddReactions | ChannelPermission.AttachFiles;
        public int MemberCount { get; set; }
        public DateTimeOffset? LastMessageAt { get; set; }
        public byte[] RowVersion { get; set; } = null!;

        // Navigation Properties
        public Attachment? Icon { get; set; }
        public ApplicationUser Creator { get; set; } = null!;
        public ApplicationUser Owner { get; set; } = null!;
        public ICollection<ChannelMember> Members { get; set; } = new List<ChannelMember>();
        public ICollection<ChannelRole> Roles { get; set; } = new List<ChannelRole>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
        public ICollection<ModerationAction> ModerationActions { get; set; } = new List<ModerationAction>();
        public ICollection<ChannelAuditLog> AuditLogs { get; set; } = new List<ChannelAuditLog>();
    }
}
