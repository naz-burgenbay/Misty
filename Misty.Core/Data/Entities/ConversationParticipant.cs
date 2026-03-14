namespace Misty.Core.Data.Entities
{
    public class ConversationParticipant
    {
        public Guid ConversationParticipantId { get; set; }
        public Guid ConversationId { get; set; }
        public required string UserId { get; set; }
        public DateTimeOffset JoinedAt { get; set; }
        public DateTimeOffset? HiddenAt { get; set; }
        public DateTimeOffset? LastReadAt { get; set; }

        // Navigation Properties
        public Conversation Conversation { get; set; } = null!;
        public ApplicationUser? User { get; set; }
    }
}
