namespace Misty.Core.Data.Entities
{
    public class Conversation
    {
        public const int MaxParticipants = 2;

        public Guid ConversationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastMessageAt { get; set; }

        // Navigation Properties
        public ICollection<ConversationParticipant> Participants { get; set; } = new List<ConversationParticipant>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }
}
