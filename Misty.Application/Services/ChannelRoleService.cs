using FluentValidation;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs.Channels;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Application.Interfaces.Channels;
using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Application.Services;

public class ChannelRoleService : IChannelRoleService
{
    private readonly IChannelRepository _channelRepository;
    private readonly IValidator<CreateChannelRoleRequest> _createValidator;
    private readonly IValidator<UpdateChannelRoleRequest> _updateValidator;
    private readonly ILogger<ChannelRoleService> _logger;

    public ChannelRoleService(
        IChannelRepository channelRepository,
        IValidator<CreateChannelRoleRequest> createValidator,
        IValidator<UpdateChannelRoleRequest> updateValidator,
        ILogger<ChannelRoleService> logger)
    {
        _channelRepository = channelRepository;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _logger = logger;
    }

    // UC-7.1 List Channel Roles
    public async Task<IReadOnlyList<ChannelRoleResponse>> GetRolesAsync(
        Guid channelId, string userId, CancellationToken ct = default)
    {
        await GetRequiredActiveMemberAsync(channelId, userId, ct);

        var roles = await _channelRepository.GetChannelRolesAsync(channelId, ct);
        var counts = await _channelRepository.GetAssignedMemberCountsAsync(
            roles.Select(r => r.ChannelRoleId), ct);

        return roles.Select(role => ToRoleResponse(
            role, counts.GetValueOrDefault(role.ChannelRoleId))).ToList();
    }

    // UC-7.2 Get Channel Role
    public async Task<ChannelRoleResponse> GetRoleAsync(
        Guid channelId, Guid roleId, string userId, CancellationToken ct = default)
    {
        await GetRequiredActiveMemberAsync(channelId, userId, ct);

        var role = await _channelRepository.GetRoleByIdAsync(roleId, ct);
        if (role is null || role.ChannelId != channelId)
            throw new NotFoundException("ChannelRole", roleId);

        var count = await _channelRepository.GetAssignedMemberCountAsync(roleId, ct);
        return ToRoleResponse(role, count);
    }

    // UC-7.3 Create Channel Role
    public async Task<ChannelRoleResponse> CreateRoleAsync(
        Guid channelId, string userId, CreateChannelRoleRequest request, CancellationToken ct = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);

        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.ManageRoles);

        var role = new ChannelRole
        {
            ChannelRoleId = Guid.NewGuid(),
            ChannelId = channelId,
            Name = request.Name,
            Permissions = request.Permissions,
            Position = request.Position,
            IsSystemRole = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _channelRepository.AddRoleAsync(role, ct);
        await AddAuditLogAsync(channelId, userId, AuditAction.RoleCreated, ct,
            "ChannelRole", role.ChannelRoleId.ToString());
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Role {RoleId} created in channel {ChannelId} by {UserId}",
            role.ChannelRoleId, channelId, userId);

        return ToRoleResponse(role, 0);
    }

    // UC-7.4 Update Channel Role
    public async Task<ChannelRoleResponse> UpdateRoleAsync(
        Guid channelId, Guid roleId, string userId,
        UpdateChannelRoleRequest request, CancellationToken ct = default)
    {
        await _updateValidator.ValidateAndThrowAsync(request, ct);

        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.ManageRoles);

        var role = await _channelRepository.GetRoleByIdAsync(roleId, ct);
        if (role is null || role.ChannelId != channelId)
            throw new NotFoundException("ChannelRole", roleId);

        if (role.IsSystemRole)
        {
            // System roles: only Position is editable
            if (request.Name is not null || request.Permissions.HasValue)
                throw new BusinessRuleException("Cannot modify the name or permissions of a system role.");
        }

        if (request.Name is not null)
            role.Name = request.Name;

        if (request.Permissions.HasValue)
            role.Permissions = request.Permissions.Value;

        if (request.Position.HasValue)
            role.Position = request.Position.Value;

        role.Version = request.Version;

        await AddAuditLogAsync(channelId, userId, AuditAction.RoleUpdated, ct,
            "ChannelRole", roleId.ToString());
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Role {RoleId} updated in channel {ChannelId} by {UserId}",
            roleId, channelId, userId);

        var count = await _channelRepository.GetAssignedMemberCountAsync(roleId, ct);
        return ToRoleResponse(role, count);
    }

    // UC-7.5 Delete Channel Role
    public async Task DeleteRoleAsync(
        Guid channelId, Guid roleId, string userId, byte[] version, CancellationToken ct = default)
    {
        var member = await GetRequiredActiveMemberAsync(channelId, userId, ct);
        PermissionHelper.EnsurePermission(member, ChannelPermission.ManageRoles);

        var role = await _channelRepository.GetRoleByIdAsync(roleId, ct);
        if (role is null || role.ChannelId != channelId)
            throw new NotFoundException("ChannelRole", roleId);

        if (role.IsSystemRole)
            throw new BusinessRuleException("System roles cannot be deleted.");

        role.Version = version;
        _channelRepository.RemoveRole(role);

        await AddAuditLogAsync(channelId, userId, AuditAction.RoleDeleted, ct,
            "ChannelRole", roleId.ToString());
        await _channelRepository.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Role {RoleId} deleted from channel {ChannelId} by {UserId}",
            roleId, channelId, userId);
    }

    // Helpers

    private async Task<ChannelMember> GetRequiredActiveMemberAsync(
        Guid channelId, string userId, CancellationToken ct)
    {
        return await _channelRepository.GetActiveMemberAsync(channelId, userId, ct)
            ?? throw new NotFoundException("Channel", channelId);
    }

    private async Task AddAuditLogAsync(
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

    private static ChannelRoleResponse ToRoleResponse(ChannelRole role, int assignedMemberCount)
    {
        return new ChannelRoleResponse
        {
            ChannelRoleId = role.ChannelRoleId,
            Name = role.Name,
            Permissions = role.Permissions,
            Position = role.Position,
            IsSystemRole = role.IsSystemRole,
            AssignedMemberCount = assignedMemberCount
        };
    }
}
