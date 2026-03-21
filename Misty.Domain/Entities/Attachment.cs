using Misty.Domain.Enums;

namespace Misty.Domain.Entities
{
    public class Attachment
    {
        public Guid AttachmentId { get; set; }
        public string? UploadedByUserId { get; set; }
        public Guid? MessageId { get; set; }
        public AttachmentPurpose Purpose { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string StoragePath { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTimeOffset UploadedAt { get; set; }

        // Navigation Properties
        public User? UploadedBy { get; set; }
        public Message? Message { get; set; }
    }
}
