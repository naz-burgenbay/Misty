using FluentValidation;
using Misty.Application.DTOs.Channels;

namespace Misty.Application.Validation.Channels;

public class CreateChannelRequestValidator : AbstractValidator<CreateChannelRequest>
{
    public CreateChannelRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .Must(v => !string.IsNullOrWhiteSpace(v))
            .WithMessage("'{PropertyName}' must not be whitespace-only.");

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);
    }
}
