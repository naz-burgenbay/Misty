using FluentAssertions;
using Misty.Application.Exceptions;
using Misty.Application.Services;
using Misty.Domain.Enums;
using Misty.Tests.Common;

namespace Misty.Tests.Application.Services;

public class PermissionHelperTests
{
    // GetEffectivePermissions

    [Fact]
    public void GetEffectivePermissions_RegularMember_ReturnsDefaultPermissions()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);
        var user = TestData.User();
        var member = TestData.Member(channel, user);

        var perms = PermissionHelper.GetEffectivePermissions(member);

        perms.Should().Be(channel.DefaultPermissions);
    }

    [Fact]
    public void GetEffectivePermissions_Owner_GetsAllPermissions()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);
        var member = TestData.Member(channel, owner);

        var perms = PermissionHelper.GetEffectivePermissions(member);

        perms.HasFlag(ChannelPermission.Administrator).Should().BeTrue();
        perms.HasFlag(ChannelPermission.BanUsers).Should().BeTrue();
        perms.HasFlag(ChannelPermission.ManageRoles).Should().BeTrue();
    }

    [Fact]
    public void GetEffectivePermissions_WithRole_MergesRolePermissions()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId, ChannelPermission.SendMessages);
        var user = TestData.User();
        var member = TestData.Member(channel, user);
        var modRole = TestData.Role(channel, "Moderator", ChannelPermission.DeleteMessages | ChannelPermission.MuteUsers, 50);
        TestData.AssignRole(member, modRole);

        var perms = PermissionHelper.GetEffectivePermissions(member);

        perms.HasFlag(ChannelPermission.SendMessages).Should().BeTrue();
        perms.HasFlag(ChannelPermission.DeleteMessages).Should().BeTrue();
        perms.HasFlag(ChannelPermission.MuteUsers).Should().BeTrue();
        // Should NOT have permissions not granted by default or role
        perms.HasFlag(ChannelPermission.BanUsers).Should().BeFalse();
    }

    [Fact]
    public void GetEffectivePermissions_MultipleRoles_UnionOfAll()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId, ChannelPermission.None);
        var user = TestData.User();
        var member = TestData.Member(channel, user);

        var role1 = TestData.Role(channel, "Role1", ChannelPermission.SendMessages, 10);
        var role2 = TestData.Role(channel, "Role2", ChannelPermission.BanUsers, 20);
        TestData.AssignRole(member, role1);
        TestData.AssignRole(member, role2);

        var perms = PermissionHelper.GetEffectivePermissions(member);

        perms.HasFlag(ChannelPermission.SendMessages).Should().BeTrue();
        perms.HasFlag(ChannelPermission.BanUsers).Should().BeTrue();
    }

    [Fact]
    public void GetEffectivePermissions_AdministratorFlag_GrantsAllPermissions()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId, ChannelPermission.None);
        var user = TestData.User();
        var member = TestData.Member(channel, user);
        var adminRole = TestData.Role(channel, "Admin", ChannelPermission.Administrator, 100);
        TestData.AssignRole(member, adminRole);

        var perms = PermissionHelper.GetEffectivePermissions(member);

        perms.HasFlag(ChannelPermission.BanUsers).Should().BeTrue();
        perms.HasFlag(ChannelPermission.ManageRoles).Should().BeTrue();
        perms.HasFlag(ChannelPermission.EditChannel).Should().BeTrue();
    }

    // EnsurePermission

    [Fact]
    public void EnsurePermission_HasPermission_DoesNotThrow()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId, ChannelPermission.SendMessages);
        var user = TestData.User();
        var member = TestData.Member(channel, user);

        var act = () => PermissionHelper.EnsurePermission(member, ChannelPermission.SendMessages);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsurePermission_MissingPermission_ThrowsBusinessRuleException()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId, ChannelPermission.SendMessages);
        var user = TestData.User();
        var member = TestData.Member(channel, user);

        var act = () => PermissionHelper.EnsurePermission(member, ChannelPermission.BanUsers);

        act.Should().Throw<BusinessRuleException>()
            .WithMessage("*BanUsers*");
    }

    // GetHighestRolePosition

    [Fact]
    public void GetHighestRolePosition_Owner_ReturnsIntMaxValue()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);
        var member = TestData.Member(channel, owner);

        PermissionHelper.GetHighestRolePosition(member).Should().Be(int.MaxValue);
    }

    [Fact]
    public void GetHighestRolePosition_NoRoles_ReturnsZero()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);
        var user = TestData.User();
        var member = TestData.Member(channel, user);

        PermissionHelper.GetHighestRolePosition(member).Should().Be(0);
    }

    [Fact]
    public void GetHighestRolePosition_MultipleRoles_ReturnsHighest()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);
        var user = TestData.User();
        var member = TestData.Member(channel, user);

        var role1 = TestData.Role(channel, "Low", ChannelPermission.None, 10);
        var role2 = TestData.Role(channel, "High", ChannelPermission.None, 50);
        TestData.AssignRole(member, role1);
        TestData.AssignRole(member, role2);

        PermissionHelper.GetHighestRolePosition(member).Should().Be(50);
    }

    // EnsureOutranks

    [Fact]
    public void EnsureOutranks_HigherPosition_DoesNotThrow()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);

        var actor = TestData.Member(channel, TestData.User());
        var target = TestData.Member(channel, TestData.User());

        var highRole = TestData.Role(channel, "Senior", ChannelPermission.None, 50);
        var lowRole = TestData.Role(channel, "Junior", ChannelPermission.None, 10);
        TestData.AssignRole(actor, highRole);
        TestData.AssignRole(target, lowRole);

        var act = () => PermissionHelper.EnsureOutranks(actor, target);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureOutranks_EqualPosition_Throws()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);

        var actor = TestData.Member(channel, TestData.User());
        var target = TestData.Member(channel, TestData.User());

        var role = TestData.Role(channel, "Same", ChannelPermission.None, 50);
        TestData.AssignRole(actor, role);
        TestData.AssignRole(target, role);

        var act = () => PermissionHelper.EnsureOutranks(actor, target);

        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void EnsureOutranks_LowerPosition_Throws()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);

        var actor = TestData.Member(channel, TestData.User());
        var target = TestData.Member(channel, TestData.User());

        var lowRole = TestData.Role(channel, "Low", ChannelPermission.None, 10);
        var highRole = TestData.Role(channel, "High", ChannelPermission.None, 50);
        TestData.AssignRole(actor, lowRole);
        TestData.AssignRole(target, highRole);

        var act = () => PermissionHelper.EnsureOutranks(actor, target);

        act.Should().Throw<BusinessRuleException>();
    }

    [Fact]
    public void EnsureOutranks_OwnerAlwaysOutranks()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);

        var ownerMember = TestData.Member(channel, owner);
        var target = TestData.Member(channel, TestData.User());
        var highRole = TestData.Role(channel, "High", ChannelPermission.None, 999);
        TestData.AssignRole(target, highRole);

        var act = () => PermissionHelper.EnsureOutranks(ownerMember, target);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureOutranks_NonOwnerCannotOutrankOwner()
    {
        var owner = TestData.User();
        var channel = TestData.Channel(owner.UserId);

        var ownerMember = TestData.Member(channel, owner);
        var actor = TestData.Member(channel, TestData.User());
        var highRole = TestData.Role(channel, "High", ChannelPermission.None, 999);
        TestData.AssignRole(actor, highRole);

        var act = () => PermissionHelper.EnsureOutranks(actor, ownerMember);

        act.Should().Throw<BusinessRuleException>();
    }
}
