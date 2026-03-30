using FluentValidation;
using Misty.Application.DTOs.Channels;

namespace Misty.Application.Validation.Channels;

public class CreateModerationActionRequestValidator : AbstractValidator<CreateModerationActionRequest>
{
    public CreateModerationActionRequestValidator()
    {
        RuleFor(x => x.TargetUserId)
            .NotEmpty();

        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(1000)
            .Must(v => !string.IsNullOrWhiteSpace(v))
            .WithMessage("'{PropertyName}' must not be whitespace-only.");

        RuleFor(x => x.ExpiresAt)
            .GreaterThan(DateTimeOffset.UtcNow)
            .WithMessage("'{PropertyName}' must be in the future.")
            .When(x => x.ExpiresAt.HasValue);
    }
}
