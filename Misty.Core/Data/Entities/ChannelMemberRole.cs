namespace Misty.Core.Data.Entities
{
    public class ChannelMemberRole
    {
        public Guid ChannelMemberId { get; set; }
        public Guid ChannelRoleId { get; set; }
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public ChannelMember Member { get; set; } = null!;
        public ChannelRole Role { get; set; } = null!;
    }
}
