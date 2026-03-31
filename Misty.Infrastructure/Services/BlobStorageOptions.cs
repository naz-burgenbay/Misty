namespace Misty.Infrastructure.Services;

public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    public required string ConnectionString { get; set; }
    public required string ContainerName { get; set; }
}
