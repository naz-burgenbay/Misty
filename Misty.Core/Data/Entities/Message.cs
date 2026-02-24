namespace Misty.Core.Data.Entities
{
    public class Message
    {
        public Guid MessageId { get; set; }
        public string? AuthorUserId { get; set; }
        public Guid? ChannelId { get; set; }
        public Guid? ConversationId { get; set; }
        public string Content { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public DateTime? EditedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? ParentMessageId { get; set; }

        // Navigation Properties
        public ApplicationUser? Author { get; set; }
        public Channel? Channel { get; set; }
        public Conversation? Conversation { get; set; }
        public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
        public ICollection<Message> Replies { get; set; } = new List<Message>();
        public Message? ParentMessage { get; set; }
    }
}
