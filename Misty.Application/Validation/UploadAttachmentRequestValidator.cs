using FluentValidation;
using Misty.Application.DTOs;
using Misty.Domain.Enums;

namespace Misty.Application.Validation;

public class UploadAttachmentRequestValidator : AbstractValidator<UploadAttachmentRequest>
{
    private static readonly HashSet<string> ImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/gif",
        "image/webp"
    };

    public UploadAttachmentRequestValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(256)
            .Must(v => !string.IsNullOrWhiteSpace(v))
            .WithMessage("'{PropertyName}' must not be whitespace-only.");

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .MaximumLength(256);

        RuleFor(x => x.ContentType)
            .Must(ct => ImageContentTypes.Contains(ct))
            .When(x => x.Purpose is AttachmentPurpose.UserAvatar or AttachmentPurpose.ChannelIcon)
            .WithMessage("Content type must be an image type (image/jpeg, image/png, image/gif, image/webp) for avatars and channel icons.");

        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0);

        RuleFor(x => x.Purpose)
            .IsInEnum();
    }
}
