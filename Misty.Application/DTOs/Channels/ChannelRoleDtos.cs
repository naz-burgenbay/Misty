using Misty.Domain.Enums;

namespace Misty.Application.DTOs.Channels;

public record ChannelRoleSummary
{
    public Guid ChannelRoleId { get; init; }
    public required string Name { get; init; }
    public int Position { get; init; }
}

public record ChannelRoleResponse
{
    public Guid ChannelRoleId { get; init; }
    public required string Name { get; init; }
    public ChannelPermission Permissions { get; init; }
    public int Position { get; init; }
    public bool IsSystemRole { get; init; }
    public int AssignedMemberCount { get; init; }
}

public record CreateChannelRoleRequest
{
    public string Name { get; init; } = default!;
    public ChannelPermission Permissions { get; init; }
    public int Position { get; init; }
}

public record UpdateChannelRoleRequest
{
    public string? Name { get; init; }
    public ChannelPermission? Permissions { get; init; }
    public int? Position { get; init; }
    public byte[] Version { get; init; } = default!;
}
