using FluentValidation;
using Misty.Application.DTOs;

namespace Misty.Application.Validation;

public class AddReactionRequestValidator : AbstractValidator<AddReactionRequest>
{
    public AddReactionRequestValidator()
    {
        RuleFor(x => x.Emoji)
            .NotEmpty()
            .MaximumLength(64)
            .Must(v => !string.IsNullOrWhiteSpace(v))
            .WithMessage("'{PropertyName}' must not be whitespace-only.");
    }
}
