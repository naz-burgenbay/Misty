namespace Misty.Application.DTOs;

public record AttachmentResponse
{
    public Guid AttachmentId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long FileSizeBytes { get; init; }
    public required string Url { get; init; }
}
