namespace Misty.Core.Data.Entities
{
    public class ConversationParticipant
    {
        public Guid ConversationParticipantId { get; set; }
        public Guid ConversationId { get; set; }
        public required string UserId { get; set; }
        public DateTime JoinedAt { get; set; }
        public DateTime? HiddenAt { get; set; }
        public DateTime? LastReadAt { get; set; }

        // Navigation Properties
        public Conversation Conversation { get; set; } = null!;
        public ApplicationUser? User { get; set; }
    }
}
