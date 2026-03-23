using Misty.Application.DTOs;
using Misty.Domain.Enums;

namespace Misty.Application.Interfaces;

public interface IAttachmentService
{
    Task<AttachmentResponse> UploadAsync(Stream content, string fileName, string contentType, long fileSizeBytes, AttachmentPurpose purpose, string userId, CancellationToken ct = default);
    Task DeleteAsync(Guid attachmentId, string userId, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(Guid attachmentId, CancellationToken ct = default);
}
