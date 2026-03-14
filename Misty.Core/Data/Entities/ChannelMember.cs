namespace Misty.Core.Data.Entities
{
    public class ChannelMember
    {
        public Guid ChannelMemberId { get; set; }
        public required string UserId { get; set; }
        public Guid ChannelId { get; set; }
        public DateTimeOffset JoinedAt { get; set; }
        public DateTimeOffset? LeftAt { get; set; }
        public DateTimeOffset? LastReadAt { get; set; }

        // Navigation Properties
        public ApplicationUser User { get; set; } = null!;
        public Channel Channel { get; set; } = null!;
        public ICollection<ChannelMemberRole> AssignedRoles { get; set; } = new List<ChannelMemberRole>();
    }
}
