namespace Misty.Domain.Entities
{
    public class UserBlock
    {
        public Guid UserBlockId { get; set; }
        public required string BlockingUserId { get; set; }
        public required string BlockedUserId { get; set; }
        public DateTimeOffset BlockedAt { get; set; }

        // Navigation Properties
        public User BlockingUser { get; set; } = null!;
        public User BlockedUser { get; set; } = null!;
    }
}
