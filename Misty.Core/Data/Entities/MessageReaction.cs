namespace Misty.Core.Data.Entities
{
    public class MessageReaction
    {
        public Guid MessageReactionId { get; set; }
        public Guid MessageId { get; set; }
        public required string ReactedByUserId { get; set; }
        public string Emoji { get; set; } = string.Empty;
        public DateTime ReactedAt { get; set; }

        // Navigation Properties
        public Message Message { get; set; } = null!;
        public ApplicationUser? User { get; set; }
    }
}
