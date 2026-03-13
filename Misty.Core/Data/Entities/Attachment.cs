using Misty.Core.Enums;

namespace Misty.Core.Data.Entities
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
        public DateTime UploadedAt { get; set; }

        // Navigation Properties
        public ApplicationUser? UploadedBy { get; set; }
        public Message? Message { get; set; }
    }
}
