namespace Misty.Application.DTOs.Channels;

public record ChannelMemberResponse
{
    public Guid ChannelMemberId { get; init; }
    public required UserSummary User { get; init; }
    public DateTimeOffset JoinedAt { get; init; }
    public IReadOnlyList<ChannelRoleSummary> Roles { get; init; } = [];
}

public record MarkChannelReadRequest
{
    public DateTimeOffset LastReadAt { get; init; }
}

public record UpdateChannelMemberRolesRequest
{
    public List<Guid> RoleIds { get; init; } = [];
}
