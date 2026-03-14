using Misty.Application.Enums;

namespace Misty.Core.Data.Entities
{
    public class ChannelRole
    {
        public Guid ChannelRoleId { get; set; }
        public Guid ChannelId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsSystemRole { get; set; }
        public ChannelPermission Permissions { get; set; }
        public int Position { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public byte[] RowVersion { get; set; } = null!;

        // Navigation Properties
        public Channel Channel { get; set; } = null!;
        public ICollection<ChannelMemberRole> MemberAssignments { get; set; } = new List<ChannelMemberRole>();
    }
}
