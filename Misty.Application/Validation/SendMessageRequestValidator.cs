using FluentValidation;
using Misty.Application.DTOs;

namespace Misty.Application.Validation;

public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty()
            .MaximumLength(4000)
            .Must(v => !string.IsNullOrWhiteSpace(v))
            .WithMessage("'{PropertyName}' must not be whitespace-only.");

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty();
    }
}
