namespace Misty.Core.Data.Entities
{
    public class ChannelMember
    {
        public Guid ChannelMemberId { get; set; }
        public required string UserId { get; set; }
        public Guid ChannelId { get; set; }
        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public ApplicationUser User { get; set; } = null!;
        public Channel Channel { get; set; } = null!;
        public ICollection<ChannelMemberRole> AssignedRoles { get; set; } = new List<ChannelMemberRole>();
    }
}
