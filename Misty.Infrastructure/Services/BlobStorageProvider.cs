using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Misty.Application.Interfaces;

namespace Misty.Infrastructure.Services;

public sealed class BlobStorageProvider : IBlobStorageProvider
{
    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobStorageProvider> _logger;
    private bool _containerEnsured;

    public BlobStorageProvider(
        BlobServiceClient blobServiceClient,
        BlobStorageOptions options,
        ILogger<BlobStorageProvider> logger)
    {
        _container = blobServiceClient.GetBlobContainerClient(options.ContainerName);
        _logger = logger;
    }

    public async Task<string> UploadAsync(
        Stream content, string storagePath, string contentType, CancellationToken ct = default)
    {
        await EnsureContainerExistsAsync(ct);

        var blob = _container.GetBlobClient(storagePath);

        try
        {
            await blob.UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            }, ct);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to upload blob at {StoragePath}", storagePath);
            throw new InvalidOperationException($"Blob upload failed for '{storagePath}'.", ex);
        }

        _logger.LogInformation("Blob uploaded at {StoragePath}", storagePath);
        return storagePath;
    }

    public async Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        await EnsureContainerExistsAsync(ct);

        var blob = _container.GetBlobClient(storagePath);

        try
        {
            await blob.DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: ct);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to delete blob at {StoragePath}", storagePath);
            throw new InvalidOperationException($"Blob delete failed for '{storagePath}'.", ex);
        }

        _logger.LogInformation("Blob deleted at {StoragePath}", storagePath);
    }

    public async Task<string> GetDownloadUrlAsync(string storagePath, CancellationToken ct = default)
    {
        await EnsureContainerExistsAsync(ct);

        var blob = _container.GetBlobClient(storagePath);

        if (blob.CanGenerateSasUri)
        {
            var sasUri = blob.GenerateSasUri(BlobSasPermissions.Read, DateTimeOffset.UtcNow.AddHours(1));
            return sasUri.AbsoluteUri;
        }

        return blob.Uri.AbsoluteUri;
    }

    private async Task EnsureContainerExistsAsync(CancellationToken ct)
    {
        if (_containerEnsured) return;

        try
        {
            await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
            _containerEnsured = true;
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to ensure blob container exists");
            throw new InvalidOperationException("Could not initialize blob storage container.", ex);
        }
    }
}
