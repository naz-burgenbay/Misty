using FluentValidation;
using Misty.Application.DTOs.Channels;

namespace Misty.Application.Validation.Channels;

public class UpdateChannelRequestValidator : AbstractValidator<UpdateChannelRequest>
{
    public UpdateChannelRequestValidator()
    {
        RuleFor(x => x.Name)
            .MinimumLength(1)
            .MaximumLength(100)
            .Must(v => !string.IsNullOrWhiteSpace(v))
            .WithMessage("'{PropertyName}' must not be whitespace-only.")
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x.Version)
            .NotEmpty();
    }
}
