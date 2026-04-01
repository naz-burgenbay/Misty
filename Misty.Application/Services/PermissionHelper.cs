using Misty.Application.Exceptions;
using Misty.Domain.Entities;
using Misty.Domain.Enums;

namespace Misty.Application.Services;

public static class PermissionHelper
{
    public static ChannelPermission GetEffectivePermissions(ChannelMember member)
    {
        var perms = member.Channel.DefaultPermissions;

        foreach (var assignedRole in member.AssignedRoles)
            perms |= assignedRole.Role.Permissions;

        if (member.Channel.OwnerUserId == member.UserId)
            perms |= (ChannelPermission)~0L;

        if (perms.HasFlag(ChannelPermission.Administrator))
            perms |= (ChannelPermission)~0L;

        return perms;
    }

    public static void EnsurePermission(ChannelMember member, ChannelPermission required)
    {
        var effective = GetEffectivePermissions(member);
        if (!effective.HasFlag(required))
            throw new BusinessRuleException($"You do not have the required permission: {required}.");
    }

    public static int GetHighestRolePosition(ChannelMember member)
    {
        if (member.Channel.OwnerUserId == member.UserId)
            return int.MaxValue;

        var max = 0;
        foreach (var ar in member.AssignedRoles)
        {
            if (ar.Role.Position > max)
                max = ar.Role.Position;
        }
        return max;
    }

    public static void EnsureOutranks(ChannelMember actor, ChannelMember target)
    {
        if (GetHighestRolePosition(actor) <= GetHighestRolePosition(target))
            throw new BusinessRuleException("You cannot perform this action on a member with an equal or higher role.");
    }
}
