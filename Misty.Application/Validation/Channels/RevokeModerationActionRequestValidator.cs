using FluentValidation;
using Misty.Application.DTOs.Channels;

namespace Misty.Application.Validation.Channels;

public class RevokeModerationActionRequestValidator : AbstractValidator<RevokeModerationActionRequest>
{
    public RevokeModerationActionRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(1000)
            .Must(v => !string.IsNullOrWhiteSpace(v))
            .WithMessage("'{PropertyName}' must not be whitespace-only.")
            .When(x => x.Reason is not null);
    }
}
