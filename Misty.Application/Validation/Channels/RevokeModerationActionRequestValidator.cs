using FluentValidation;
using Misty.Application.DTOs.Channels;

namespace Misty.Application.Validation.Channels;

public class RevokeModerationActionRequestValidator : AbstractValidator<RevokeModerationActionRequest>
{
    public RevokeModerationActionRequestValidator()
    {
        RuleFor(x => x.Reason)
            .MaximumLength(1000)
            .When(x => x.Reason is not null);
    }
}
