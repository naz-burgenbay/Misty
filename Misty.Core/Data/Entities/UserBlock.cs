namespace Misty.Core.Data.Entities
{
    public class UserBlock
    {
        public Guid UserBlockId { get; set; }
        public required string BlockingUserId { get; set; }
        public required string BlockedUserId { get; set; }
        public DateTime BlockedAt { get; set; }

        // Navigation Properties
        public ApplicationUser? BlockingUser { get; set; }
        public ApplicationUser? BlockedUser { get; set; }
    }
}
