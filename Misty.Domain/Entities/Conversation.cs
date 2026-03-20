namespace Misty.Domain.Entities
{
    public class Conversation
    {
        public const int MaxParticipants = 2;

        public Guid ConversationId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastMessageAt { get; set; }

        // Navigation Properties
        public ICollection<ConversationParticipant> Participants { get; set; } = new List<ConversationParticipant>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }
}
