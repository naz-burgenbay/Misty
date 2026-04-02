using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Application.Services;
using Misty.Domain.Entities;
using Misty.Domain.Enums;
using Misty.Tests.Common;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Misty.Tests.Application.Services;

public class AttachmentServiceTests
{
    private readonly IAttachmentRepository _attachmentRepo = Substitute.For<IAttachmentRepository>();
    private readonly IBlobStorageProvider _blobStorage = Substitute.For<IBlobStorageProvider>();
    private readonly IValidator<UploadAttachmentRequest> _uploadValidator = Substitute.For<IValidator<UploadAttachmentRequest>>();
    private readonly AttachmentService _sut;

    private readonly User _user;

    public AttachmentServiceTests()
    {
        _sut = new AttachmentService(
            _attachmentRepo, _blobStorage, _uploadValidator,
            Substitute.For<ILogger<AttachmentService>>());

        _uploadValidator
            .ValidateAsync(Arg.Any<ValidationContext<UploadAttachmentRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        _user = TestData.User(displayName: "Alice");
    }

    // UC-2.1 Upload Attachment

    [Fact]
    public async Task Upload_ValidRequest_UploadsAndReturnsResponse()
    {
        var request = CreateUploadRequest();
        using var stream = new MemoryStream();
        _blobStorage.GetDownloadUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://cdn.test/file.png");

        var result = await _sut.UploadAsync(stream, request, _user.UserId);

        result.FileName.Should().Be(request.FileName);
        result.ContentType.Should().Be(request.ContentType);
        result.FileSizeBytes.Should().Be(request.FileSizeBytes);
        result.Url.Should().Be("https://cdn.test/file.png");
        result.AttachmentId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Upload_PersistsAttachmentToRepository()
    {
        var request = CreateUploadRequest();
        using var stream = new MemoryStream();
        _blobStorage.GetDownloadUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://cdn.test/file.png");

        await _sut.UploadAsync(stream, request, _user.UserId);

        await _attachmentRepo.Received(1).AddAsync(
            Arg.Is<Attachment>(a =>
                a.UploadedByUserId == _user.UserId
                && a.Purpose == request.Purpose
                && a.FileName == request.FileName
                && a.ContentType == request.ContentType
                && a.FileSizeBytes == request.FileSizeBytes),
            Arg.Any<CancellationToken>());
        await _attachmentRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_StoragePathContainsUserIdAndAttachmentId()
    {
        var request = CreateUploadRequest();
        using var stream = new MemoryStream();
        _blobStorage.GetDownloadUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://cdn.test/file.png");

        var result = await _sut.UploadAsync(stream, request, _user.UserId);

        await _blobStorage.Received(1).UploadAsync(
            stream,
            Arg.Is<string>(p => p.Contains(_user.UserId) && p.Contains(result.AttachmentId.ToString())),
            request.ContentType,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_BlobUploadedBeforeDbPersist()
    {
        var request = CreateUploadRequest();
        using var stream = new MemoryStream();
        _blobStorage.GetDownloadUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("https://cdn.test/file.png");

        await _sut.UploadAsync(stream, request, _user.UserId);

        Received.InOrder(() =>
        {
            _blobStorage.UploadAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
            _attachmentRepo.AddAsync(Arg.Any<Attachment>(), Arg.Any<CancellationToken>());
            _attachmentRepo.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Upload_BlobFails_DoesNotPersistToDb()
    {
        var request = CreateUploadRequest();
        using var stream = new MemoryStream();
        _blobStorage.UploadAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Blob upload failed"));

        var act = () => _sut.UploadAsync(stream, request, _user.UserId);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _attachmentRepo.DidNotReceive().AddAsync(Arg.Any<Attachment>(), Arg.Any<CancellationToken>());
        await _attachmentRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_DbSaveFails_DoesNotDeleteOrphanedBlob()
    {
        var request = CreateUploadRequest();
        using var stream = new MemoryStream();
        _attachmentRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB commit failed"));

        var act = () => _sut.UploadAsync(stream, request, _user.UserId);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _blobStorage.Received(1).UploadAsync(
            stream, Arg.Any<string>(), request.ContentType, Arg.Any<CancellationToken>());
        await _blobStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Upload_ValidationFails_Throws()
    {
        _uploadValidator
            .ValidateAsync(Arg.Any<ValidationContext<UploadAttachmentRequest>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ValidationException(new[] { new ValidationFailure("FileName", "Required") }));

        var request = CreateUploadRequest();
        using var stream = new MemoryStream();

        var act = () => _sut.UploadAsync(stream, request, _user.UserId);

        await act.Should().ThrowAsync<ValidationException>();
        await _blobStorage.DidNotReceive().UploadAsync(
            Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // UC-2.2 Delete Attachment

    [Fact]
    public async Task Delete_OwnAttachment_DeletesFromDbAndBlob()
    {
        var attachment = CreateAttachment(_user.UserId);
        _attachmentRepo.GetByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);

        await _sut.DeleteAsync(attachment.AttachmentId, _user.UserId);

        await _attachmentRepo.Received(1).DeleteAsync(attachment, Arg.Any<CancellationToken>());
        await _attachmentRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _blobStorage.Received(1).DeleteAsync(attachment.StoragePath, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_DbPersistedBeforeBlobDelete()
    {
        var attachment = CreateAttachment(_user.UserId);
        _attachmentRepo.GetByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);

        await _sut.DeleteAsync(attachment.AttachmentId, _user.UserId);

        Received.InOrder(() =>
        {
            _attachmentRepo.DeleteAsync(attachment, Arg.Any<CancellationToken>());
            _attachmentRepo.SaveChangesAsync(Arg.Any<CancellationToken>());
            _blobStorage.DeleteAsync(attachment.StoragePath, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Delete_NotFound_Throws()
    {
        var id = Guid.NewGuid();
        _attachmentRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Attachment?)null);

        var act = () => _sut.DeleteAsync(id, _user.UserId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_NotOwner_Throws()
    {
        var other = TestData.User(displayName: "Other");
        var attachment = CreateAttachment(other.UserId);
        _attachmentRepo.GetByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);

        var act = () => _sut.DeleteAsync(attachment.AttachmentId, _user.UserId);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Delete_BlobDeleteFails_DoesNotThrow()
    {
        var attachment = CreateAttachment(_user.UserId);
        _attachmentRepo.GetByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);
        _blobStorage.DeleteAsync(attachment.StoragePath, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Blob storage down"));

        var act = () => _sut.DeleteAsync(attachment.AttachmentId, _user.UserId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Delete_SaveFails_DoesNotCallBlobDelete()
    {
        var attachment = CreateAttachment(_user.UserId);
        _attachmentRepo.GetByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);
        _attachmentRepo.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("DB commit failed"));

        var act = () => _sut.DeleteAsync(attachment.AttachmentId, _user.UserId);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _blobStorage.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // UC-2.3 Get Attachment Download URL

    [Fact]
    public async Task GetDownloadUrl_ReturnsUrl()
    {
        var attachment = CreateAttachment(_user.UserId);
        _attachmentRepo.GetByIdAsync(attachment.AttachmentId, Arg.Any<CancellationToken>())
            .Returns(attachment);
        _blobStorage.GetDownloadUrlAsync(attachment.StoragePath, Arg.Any<CancellationToken>())
            .Returns("https://cdn.test/download.png");

        var result = await _sut.GetDownloadUrlAsync(attachment.AttachmentId);

        result.Should().Be("https://cdn.test/download.png");
    }

    [Fact]
    public async Task GetDownloadUrl_NotFound_Throws()
    {
        var id = Guid.NewGuid();
        _attachmentRepo.GetByIdAsync(id, Arg.Any<CancellationToken>()).Returns((Attachment?)null);

        var act = () => _sut.GetDownloadUrlAsync(id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // Helpers

    private static UploadAttachmentRequest CreateUploadRequest() => new()
    {
        FileName = "photo.png",
        ContentType = "image/png",
        FileSizeBytes = 2048,
        Purpose = AttachmentPurpose.MessageAttachment
    };

    private static Attachment CreateAttachment(string userId) => new()
    {
        AttachmentId = Guid.NewGuid(),
        UploadedByUserId = userId,
        Purpose = AttachmentPurpose.MessageAttachment,
        FileName = "photo.png",
        StoragePath = $"users/{userId}/attachments/{Guid.NewGuid()}",
        ContentType = "image/png",
        FileSizeBytes = 2048,
        UploadedAt = DateTimeOffset.UtcNow
    };
}
