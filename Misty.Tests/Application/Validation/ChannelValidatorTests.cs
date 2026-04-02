using FluentAssertions;
using FluentValidation;
using Misty.Application.DTOs.Channels;
using Misty.Application.Validation.Channels;
using Misty.Domain.Enums;

namespace Misty.Tests.Application.Validation;

public class CreateChannelRequestValidatorTests
{
    private readonly CreateChannelRequestValidator _sut = new();

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var request = new CreateChannelRequest { Name = "General" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Name_NullOrEmptyOrWhitespace_Fails(string? name)
    {
        var request = new CreateChannelRequest { Name = name! };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task Name_TooLong_Fails()
    {
        var request = new CreateChannelRequest { Name = new string('x', 101) };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Name_AtMaxLength_Passes()
    {
        var request = new CreateChannelRequest { Name = new string('x', 100) };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Description_TooLong_Fails()
    {
        var request = new CreateChannelRequest
        {
            Name = "General",
            Description = new string('x', 501)
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Description");
    }

    [Fact]
    public async Task Description_Null_IsValid()
    {
        var request = new CreateChannelRequest { Name = "General", Description = null };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }
}

public class UpdateChannelRequestValidatorTests
{
    private readonly UpdateChannelRequestValidator _sut = new();

    [Fact]
    public async Task Valid_MinimalRequest_Passes()
    {
        var request = new UpdateChannelRequest { Version = [0, 0, 0, 1] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Name_Null_IsValid()
    {
        var request = new UpdateChannelRequest { Name = null, Version = [0, 0, 0, 1] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Name_EmptyOrWhitespace_Fails(string name)
    {
        var request = new UpdateChannelRequest { Name = name, Version = [0, 0, 0, 1] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task Name_TooLong_Fails()
    {
        var request = new UpdateChannelRequest
        {
            Name = new string('x', 101),
            Version = [0, 0, 0, 1]
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Description_TooLong_Fails()
    {
        var request = new UpdateChannelRequest
        {
            Description = new string('x', 501),
            Version = [0, 0, 0, 1]
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Version_Empty_Fails()
    {
        var request = new UpdateChannelRequest { Version = [] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Version");
    }
}

public class CreateChannelRoleRequestValidatorTests
{
    private readonly CreateChannelRoleRequestValidator _sut = new();

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var request = new CreateChannelRoleRequest
        {
            Name = "Moderator",
            Permissions = ChannelPermission.SendMessages,
            Position = 1
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Name_NullOrEmptyOrWhitespace_Fails(string? name)
    {
        var request = new CreateChannelRoleRequest { Name = name! };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task Name_TooLong_Fails()
    {
        var request = new CreateChannelRoleRequest { Name = new string('x', 101) };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }
}

public class UpdateChannelRoleRequestValidatorTests
{
    private readonly UpdateChannelRoleRequestValidator _sut = new();

    [Fact]
    public async Task Valid_MinimalRequest_Passes()
    {
        var request = new UpdateChannelRoleRequest { Version = [0, 0, 0, 1] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Name_Null_IsValid()
    {
        var request = new UpdateChannelRoleRequest { Name = null, Version = [0, 0, 0, 1] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Name_EmptyOrWhitespace_Fails(string name)
    {
        var request = new UpdateChannelRoleRequest { Name = name, Version = [0, 0, 0, 1] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task Name_TooLong_Fails()
    {
        var request = new UpdateChannelRoleRequest
        {
            Name = new string('x', 101),
            Version = [0, 0, 0, 1]
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Version_Empty_Fails()
    {
        var request = new UpdateChannelRoleRequest { Version = [] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Version");
    }
}

public class CreateModerationActionRequestValidatorTests
{
    private readonly CreateModerationActionRequestValidator _sut = new();

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = "user-1",
            Reason = "Spam",
            Type = ModerationType.Mute
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task TargetUserId_NullOrEmpty_Fails(string? targetUserId)
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = targetUserId!,
            Reason = "Spam"
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "TargetUserId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reason_NullOrEmptyOrWhitespace_Fails(string? reason)
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = "user-1",
            Reason = reason!
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Reason");
    }

    [Fact]
    public async Task Reason_TooLong_Fails()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = "user-1",
            Reason = new string('x', 1001)
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Reason");
    }

    [Fact]
    public async Task Type_InvalidEnum_Fails()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = "user-1",
            Reason = "Spam",
            Type = (ModerationType)999
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Type");
    }

    [Fact]
    public async Task ExpiresAt_InPast_Fails()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = "user-1",
            Reason = "Spam",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1)
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ExpiresAt");
    }

    [Fact]
    public async Task ExpiresAt_InFuture_Passes()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = "user-1",
            Reason = "Spam",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ExpiresAt_Null_IsValid()
    {
        var request = new CreateModerationActionRequest
        {
            TargetUserId = "user-1",
            Reason = "Spam",
            ExpiresAt = null
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }
}

public class RevokeModerationActionRequestValidatorTests
{
    private readonly RevokeModerationActionRequestValidator _sut = new();

    [Fact]
    public async Task Valid_NoReason_Passes()
    {
        var request = new RevokeModerationActionRequest { Reason = null };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Valid_WithReason_Passes()
    {
        var request = new RevokeModerationActionRequest { Reason = "Appealed" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Reason_WhitespaceOnly_Fails()
    {
        var request = new RevokeModerationActionRequest { Reason = "   " };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Reason");
    }

    [Fact]
    public async Task Reason_TooLong_Fails()
    {
        var request = new RevokeModerationActionRequest { Reason = new string('x', 1001) };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Reason");
    }
}

public class MarkChannelReadRequestValidatorTests
{
    private readonly MarkChannelReadRequestValidator _sut = new();

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var request = new MarkChannelReadRequest { LastReadAt = DateTimeOffset.UtcNow };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task LastReadAt_Default_Fails()
    {
        var request = new MarkChannelReadRequest { LastReadAt = default };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "LastReadAt");
    }
}

public class TransferOwnershipRequestValidatorTests
{
    private readonly TransferOwnershipRequestValidator _sut = new();

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var request = new TransferOwnershipRequest { NewOwnerUserId = "user-2" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NewOwnerUserId_NullOrEmpty_Fails(string? userId)
    {
        var request = new TransferOwnershipRequest { NewOwnerUserId = userId! };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "NewOwnerUserId");
    }
}

public class UpdateChannelMemberRolesRequestValidatorTests
{
    private readonly UpdateChannelMemberRolesRequestValidator _sut = new();

    [Fact]
    public async Task Valid_EmptyList_Passes()
    {
        var request = new UpdateChannelMemberRolesRequest { RoleIds = [] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Valid_WithRoles_Passes()
    {
        var request = new UpdateChannelMemberRolesRequest { RoleIds = [Guid.NewGuid()] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RoleIds_Null_Fails()
    {
        var request = new UpdateChannelMemberRolesRequest { RoleIds = null! };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "RoleIds");
    }
}
