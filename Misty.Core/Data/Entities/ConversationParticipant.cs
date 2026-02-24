namespace Misty.Core.Data.Entities
{
    public class ConversationParticipant
    {
        public Guid ConversationParticipantId { get; set; }
        public Guid ConversationId { get; set; }
        public required string UserId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Conversation Conversation { get; set; } = null!;
        public ApplicationUser User { get; set; } = null!;
    }
}
