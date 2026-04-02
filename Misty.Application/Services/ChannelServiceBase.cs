using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Application.Services;

public abstract class ChannelServiceBase
{
    private readonly IChannelRepository _channelRepository;

    protected ChannelServiceBase(IChannelRepository channelRepository)
    {
        _channelRepository = channelRepository;
    }

    protected async Task<ChannelMember> GetRequiredActiveMemberAsync(
        Guid channelId, string userId, CancellationToken ct)
    {
        return await _channelRepository.GetActiveMemberAsync(channelId, userId, ct)
            ?? throw new NotFoundException("Channel", channelId);
    }

    protected async Task AddAuditLogAsync(
        Guid channelId, string userId, AuditAction action, CancellationToken ct,
        string? targetType = null, string? targetId = null)
    {
        var auditLog = new ChannelAuditLog
        {
            ChannelAuditLogId = Guid.NewGuid(),
            ChannelId = channelId,
            ActorUserId = userId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _channelRepository.AddAuditLogAsync(auditLog, ct);
    }
}
