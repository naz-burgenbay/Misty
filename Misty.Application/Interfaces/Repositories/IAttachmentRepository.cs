using Misty.Domain.Entities;

namespace Misty.Application.Interfaces;

public interface IAttachmentRepository
{
    Task<Attachment?> GetByIdAsync(Guid attachmentId, CancellationToken ct = default);
    Task AddAsync(Attachment attachment, CancellationToken ct = default);
    Task DeleteAsync(Attachment attachment, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
