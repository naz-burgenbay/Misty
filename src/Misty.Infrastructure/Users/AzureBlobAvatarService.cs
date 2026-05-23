using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Misty.Application.Users;

namespace Misty.Infrastructure.Users;

public sealed class AzureBlobAvatarService : IAvatarService
{
    private const string ContainerName = "avatars";
    private readonly BlobServiceClient _client;

    public AzureBlobAvatarService(BlobServiceClient client) => _client = client;

    public async Task<string> UploadAsync(Guid userId, Stream content, string contentType, CancellationToken ct = default)
    {
        var container = _client.GetBlobContainerClient(ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blob = container.GetBlobClient(userId.ToString());
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);

        return blob.Uri.ToString();
    }
}
