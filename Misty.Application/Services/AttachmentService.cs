using FluentValidation;
using Microsoft.Extensions.Logging;
using Misty.Application.DTOs;
using Misty.Application.Exceptions;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;

namespace Misty.Application.Services;

public class AttachmentService : IAttachmentService
{
    private readonly IAttachmentRepository _attachmentRepository;
    private readonly IBlobStorageProvider _blobStorage;
    private readonly IValidator<UploadAttachmentRequest> _uploadValidator;
    private readonly ILogger<AttachmentService> _logger;

    public AttachmentService(
        IAttachmentRepository attachmentRepository,
        IBlobStorageProvider blobStorage,
        IValidator<UploadAttachmentRequest> uploadValidator,
        ILogger<AttachmentService> logger)
    {
        _attachmentRepository = attachmentRepository;
        _blobStorage = blobStorage;
        _uploadValidator = uploadValidator;
        _logger = logger;
    }

    // UC-2.1 Upload Attachment
    public async Task<AttachmentResponse> UploadAsync(
        Stream content, UploadAttachmentRequest request, string userId, CancellationToken ct = default)
    {
        await _uploadValidator.ValidateAndThrowAsync(request, ct);

        var attachmentId = Guid.NewGuid();
        var storagePath = $"users/{userId}/attachments/{attachmentId}";

        // Upload blob first (if this fails, no orphaned DB row is created)
        await _blobStorage.UploadAsync(content, storagePath, request.ContentType, ct);

        var attachment = new Attachment
        {
            AttachmentId = attachmentId,
            UploadedByUserId = userId,
            Purpose = request.Purpose,
            FileName = request.FileName,
            StoragePath = storagePath,
            ContentType = request.ContentType,
            FileSizeBytes = request.FileSizeBytes,
            UploadedAt = DateTimeOffset.UtcNow
        };

        await _attachmentRepository.AddAsync(attachment, ct);
        await _attachmentRepository.SaveChangesAsync(ct);

        _logger.LogInformation("Attachment {AttachmentId} uploaded by {UserId}", attachmentId, userId);

        var downloadUrl = await _blobStorage.GetDownloadUrlAsync(storagePath, ct);

        return new AttachmentResponse
        {
            AttachmentId = attachment.AttachmentId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            FileSizeBytes = attachment.FileSizeBytes,
            Url = downloadUrl
        };
    }

    // UC-2.2 Delete Attachment
    public async Task DeleteAsync(Guid attachmentId, string userId, CancellationToken ct = default)
    {
        var attachment = await _attachmentRepository.GetByIdAsync(attachmentId, ct)
            ?? throw new NotFoundException("Attachment", attachmentId);

        if (attachment.UploadedByUserId != userId)
            throw new BusinessRuleException("Only the uploader can delete this attachment.");

        // Capture path before removing entity
        var storagePath = attachment.StoragePath;

        // DB-first: remove entity and persist before external side-effects
        await _attachmentRepository.DeleteAsync(attachment, ct);
        await _attachmentRepository.SaveChangesAsync(ct);

        // External side-effect after commit
        try
        {
            await _blobStorage.DeleteAsync(storagePath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete blob for attachment {AttachmentId} at {Path}", attachmentId, storagePath);
        }

        _logger.LogInformation("Attachment {AttachmentId} deleted by {UserId}", attachmentId, userId);
    }

    // UC-2.3 Get Attachment Download URL
    public async Task<string> GetDownloadUrlAsync(Guid attachmentId, CancellationToken ct = default)
    {
        var attachment = await _attachmentRepository.GetByIdAsync(attachmentId, ct)
            ?? throw new NotFoundException("Attachment", attachmentId);

        return await _blobStorage.GetDownloadUrlAsync(attachment.StoragePath, ct);
    }
}
