using FluentAssertions;
using FluentValidation;
using Misty.Application.DTOs;
using Misty.Application.Validation;
using Misty.Domain.Enums;

namespace Misty.Tests.Application.Validation;

public class UpdateProfileRequestValidatorTests
{
    private readonly UpdateProfileRequestValidator _sut = new();

    [Fact]
    public async Task Valid_MinimalRequest_Passes()
    {
        var request = new UpdateProfileRequest { Version = [0, 0, 0, 1] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Valid_AllFields_Passes()
    {
        var request = new UpdateProfileRequest
        {
            DisplayName = "Alice",
            Bio = "Hello world",
            Version = [0, 0, 0, 1]
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task DisplayName_Null_IsValid()
    {
        var request = new UpdateProfileRequest { DisplayName = null, Version = [0, 0, 0, 1] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task DisplayName_EmptyOrWhitespace_Fails(string displayName)
    {
        var request = new UpdateProfileRequest { DisplayName = displayName, Version = [0, 0, 0, 1] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DisplayName");
    }

    [Fact]
    public async Task DisplayName_TooLong_Fails()
    {
        var request = new UpdateProfileRequest
        {
            DisplayName = new string('x', 101),
            Version = [0, 0, 0, 1]
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "DisplayName");
    }

    [Fact]
    public async Task DisplayName_AtMaxLength_Passes()
    {
        var request = new UpdateProfileRequest
        {
            DisplayName = new string('x', 100),
            Version = [0, 0, 0, 1]
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Bio_TooLong_Fails()
    {
        var request = new UpdateProfileRequest
        {
            Bio = new string('x', 501),
            Version = [0, 0, 0, 1]
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Bio");
    }

    [Fact]
    public async Task Bio_AtMaxLength_Passes()
    {
        var request = new UpdateProfileRequest
        {
            Bio = new string('x', 500),
            Version = [0, 0, 0, 1]
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Version_Empty_Fails()
    {
        var request = new UpdateProfileRequest { Version = [] };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Version");
    }
}

public class SendMessageRequestValidatorTests
{
    private readonly SendMessageRequestValidator _sut = new();

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var request = new SendMessageRequest { Content = "Hello", IdempotencyKey = "key-1" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Content_NullOrEmptyOrWhitespace_Fails(string? content)
    {
        var request = new SendMessageRequest { Content = content!, IdempotencyKey = "key-1" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Content");
    }

    [Fact]
    public async Task Content_TooLong_Fails()
    {
        var request = new SendMessageRequest
        {
            Content = new string('x', 4001),
            IdempotencyKey = "key-1"
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Content");
    }

    [Fact]
    public async Task Content_AtMaxLength_Passes()
    {
        var request = new SendMessageRequest
        {
            Content = new string('x', 4000),
            IdempotencyKey = "key-1"
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task IdempotencyKey_NullOrEmpty_Fails(string? key)
    {
        var request = new SendMessageRequest { Content = "Hello", IdempotencyKey = key! };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "IdempotencyKey");
    }
}

public class UpdateMessageRequestValidatorTests
{
    private readonly UpdateMessageRequestValidator _sut = new();

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var request = new UpdateMessageRequest { Content = "Updated" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Content_NullOrEmptyOrWhitespace_Fails(string? content)
    {
        var request = new UpdateMessageRequest { Content = content! };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Content");
    }

    [Fact]
    public async Task Content_TooLong_Fails()
    {
        var request = new UpdateMessageRequest { Content = new string('x', 4001) };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Content_AtMaxLength_Passes()
    {
        var request = new UpdateMessageRequest { Content = new string('x', 4000) };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }
}

public class AddReactionRequestValidatorTests
{
    private readonly AddReactionRequestValidator _sut = new();

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var request = new AddReactionRequest { Emoji = "👍" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Emoji_NullOrEmptyOrWhitespace_Fails(string? emoji)
    {
        var request = new AddReactionRequest { Emoji = emoji! };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Emoji");
    }

    [Fact]
    public async Task Emoji_TooLong_Fails()
    {
        var request = new AddReactionRequest { Emoji = new string('x', 65) };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Emoji_AtMaxLength_Passes()
    {
        var request = new AddReactionRequest { Emoji = new string('x', 64) };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }
}

public class CreateUserBlockRequestValidatorTests
{
    private readonly CreateUserBlockRequestValidator _sut = new();

    [Fact]
    public async Task Valid_Request_Passes()
    {
        var request = new CreateUserBlockRequest { BlockedUserId = "user-1" };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task BlockedUserId_NullOrEmpty_Fails(string? userId)
    {
        var request = new CreateUserBlockRequest { BlockedUserId = userId! };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "BlockedUserId");
    }
}

public class UploadAttachmentRequestValidatorTests
{
    private readonly UploadAttachmentRequestValidator _sut = new();

    [Fact]
    public async Task Valid_MessageAttachment_Passes()
    {
        var request = new UploadAttachmentRequest
        {
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Purpose = AttachmentPurpose.MessageAttachment
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Valid_AvatarWithImageType_Passes()
    {
        var request = new UploadAttachmentRequest
        {
            FileName = "avatar.png",
            ContentType = "image/png",
            FileSizeBytes = 2048,
            Purpose = AttachmentPurpose.UserAvatar
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    // FileName

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FileName_NullOrEmptyOrWhitespace_Fails(string? fileName)
    {
        var request = new UploadAttachmentRequest
        {
            FileName = fileName!,
            ContentType = "image/png",
            FileSizeBytes = 1024,
            Purpose = AttachmentPurpose.MessageAttachment
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FileName");
    }

    [Fact]
    public async Task FileName_TooLong_Fails()
    {
        var request = new UploadAttachmentRequest
        {
            FileName = new string('x', 257),
            ContentType = "image/png",
            FileSizeBytes = 1024,
            Purpose = AttachmentPurpose.MessageAttachment
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FileName");
    }

    // ContentType

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ContentType_NullOrEmpty_Fails(string? contentType)
    {
        var request = new UploadAttachmentRequest
        {
            FileName = "file.png",
            ContentType = contentType!,
            FileSizeBytes = 1024,
            Purpose = AttachmentPurpose.MessageAttachment
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ContentType");
    }

    [Fact]
    public async Task ContentType_TooLong_Fails()
    {
        var request = new UploadAttachmentRequest
        {
            FileName = "file.png",
            ContentType = new string('x', 257),
            FileSizeBytes = 1024,
            Purpose = AttachmentPurpose.MessageAttachment
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
    }

    // ContentType + Purpose constraints

    [Theory]
    [InlineData(AttachmentPurpose.UserAvatar)]
    [InlineData(AttachmentPurpose.ChannelIcon)]
    public async Task ContentType_NonImage_ForAvatarOrIcon_Fails(AttachmentPurpose purpose)
    {
        var request = new UploadAttachmentRequest
        {
            FileName = "file.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Purpose = purpose
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "ContentType");
    }

    [Fact]
    public async Task ContentType_NonImage_ForMessageAttachment_IsValid()
    {
        var request = new UploadAttachmentRequest
        {
            FileName = "doc.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 1024,
            Purpose = AttachmentPurpose.MessageAttachment
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/gif")]
    [InlineData("image/webp")]
    public async Task ContentType_AllowedImageTypes_ForAvatar_Pass(string contentType)
    {
        var request = new UploadAttachmentRequest
        {
            FileName = "avatar.img",
            ContentType = contentType,
            FileSizeBytes = 1024,
            Purpose = AttachmentPurpose.UserAvatar
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    // FileSizeBytes

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FileSizeBytes_ZeroOrNegative_Fails(long size)
    {
        var request = new UploadAttachmentRequest
        {
            FileName = "file.png",
            ContentType = "image/png",
            FileSizeBytes = size,
            Purpose = AttachmentPurpose.MessageAttachment
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "FileSizeBytes");
    }

    // Purpose

    [Fact]
    public async Task Purpose_InvalidEnum_Fails()
    {
        var request = new UploadAttachmentRequest
        {
            FileName = "file.png",
            ContentType = "image/png",
            FileSizeBytes = 1024,
            Purpose = (AttachmentPurpose)999
        };

        var result = await _sut.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Purpose");
    }
}
