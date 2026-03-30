namespace Misty.Application.Interfaces;

public interface IBlobStorageProvider
{
    Task<string> UploadAsync(Stream content, string storagePath, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(string storagePath, CancellationToken ct = default);
}
