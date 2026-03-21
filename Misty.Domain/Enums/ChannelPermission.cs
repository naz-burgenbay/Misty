namespace Misty.Domain.Enums;

[Flags]
public enum ChannelPermission : long
{
    None = 0,

    // Message permissions
    SendMessages       = 1L << 0,
    AddReactions       = 1L << 1,
    AttachFiles        = 1L << 2,

    // Moderation permissions
    DeleteMessages     = 1L << 10,
    MuteUsers          = 1L << 11,
    BanUsers           = 1L << 12,
    ViewAuditLog       = 1L << 13,

    // Channel management permissions
    EditChannel        = 1L << 20,
    ManageRoles        = 1L << 21,
    ManageInvites      = 1L << 22,

    // Admin
    Administrator      = 1L << 30
}
