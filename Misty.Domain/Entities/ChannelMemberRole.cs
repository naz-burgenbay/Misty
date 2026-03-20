namespace Misty.Domain.Entities
{
    public class ChannelMemberRole
    {
        public Guid ChannelMemberId { get; set; }
        public Guid ChannelRoleId { get; set; }
        public DateTimeOffset AssignedAt { get; set; }

        // Navigation Properties
        public ChannelMember Member { get; set; } = null!;
        public ChannelRole Role { get; set; } = null!;
    }
}
