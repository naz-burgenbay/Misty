using Misty.Application.DTOs;

namespace Misty.Application.Interfaces;

public interface IAttachmentService
{
    Task<AttachmentResponse> UploadAsync(Stream content, UploadAttachmentRequest request, string userId, CancellationToken ct = default);
    Task DeleteAsync(Guid attachmentId, string userId, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(Guid attachmentId, CancellationToken ct = default);
}
