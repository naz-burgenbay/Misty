namespace Misty.Domain.Entities
{
    public class Conversation
    {
        public const int MaxParticipants = 2;

        public Guid ConversationId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastMessageAt { get; set; }

        // Normalized participant pairs
        public string ParticipantLowUserId { get; set; } = string.Empty;
        public string ParticipantHighUserId { get; set; } = string.Empty;

        // Navigation Properties
        public ICollection<ConversationParticipant> Participants { get; set; } = new List<ConversationParticipant>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();

        public static (string Low, string High) NormalizeParticipants(string userIdA, string userIdB)
        {
            return string.CompareOrdinal(userIdA, userIdB) < 0
                ? (userIdA, userIdB)
                : (userIdB, userIdA);
        }
    }
}
