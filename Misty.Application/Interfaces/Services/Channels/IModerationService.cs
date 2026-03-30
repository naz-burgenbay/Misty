using Misty.Application.DTOs.Channels;
using Misty.Application.DTOs.Common;

namespace Misty.Application.Interfaces.Channels;

public interface IModerationService
{
    Task<ModerationActionResponse> CreateModerationActionAsync(Guid channelId, string userId, CreateModerationActionRequest request, CancellationToken ct = default);
    Task<ModerationActionResponse> RevokeModerationActionAsync(Guid channelId, Guid moderationActionId, string userId, RevokeModerationActionRequest request, CancellationToken ct = default);
    Task<ModerationActionResponse> GetModerationActionAsync(Guid channelId, Guid moderationActionId, string userId, CancellationToken ct = default);
    Task<PagedResponse<ModerationActionSummary>> GetModerationActionsAsync(Guid channelId, string userId, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResponse<ChannelAuditLogResponse>> GetAuditLogAsync(Guid channelId, string userId, int page, int pageSize, CancellationToken ct = default);
}
