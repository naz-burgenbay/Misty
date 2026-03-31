using Microsoft.EntityFrameworkCore;
using Misty.Application.Interfaces;
using Misty.Domain.Entities;

namespace Misty.Infrastructure.Data.Repositories;

public class AttachmentRepository : IAttachmentRepository
{
    private readonly ApplicationDbContext _db;

    public AttachmentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Attachment?> GetByIdAsync(Guid attachmentId, CancellationToken ct = default)
    {
        return await _db.Attachments.FirstOrDefaultAsync(a => a.AttachmentId == attachmentId, ct);
    }

    public async Task AddAsync(Attachment attachment, CancellationToken ct = default)
    {
        await _db.Attachments.AddAsync(attachment, ct);
    }

    public Task DeleteAsync(Attachment attachment, CancellationToken ct = default)
    {
        _db.Attachments.Remove(attachment);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        await _db.SaveChangesAsync(ct);
    }
}
