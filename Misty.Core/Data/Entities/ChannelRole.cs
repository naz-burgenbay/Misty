namespace Misty.Core.Data.Entities
{
    public class ChannelRole
    {
        public Guid ChannelRoleId { get; set; }
        public Guid ChannelId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsSystemRole { get; set; }
        public bool CanDeleteMessages { get; set; }
        public bool CanMuteUsers { get; set; }
        public bool CanBanUsers { get; set; }
        public bool CanManageRoles { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation Properties
        public Channel Channel { get; set; } = null!;
        public ICollection<ChannelMemberRole> MemberAssignments { get; set; } = new List<ChannelMemberRole>();
    }
}
