using FluentValidation;
using Misty.Application.DTOs;

namespace Misty.Application.Validation;

public class CreateUserBlockRequestValidator : AbstractValidator<CreateUserBlockRequest>
{
    public CreateUserBlockRequestValidator()
    {
        RuleFor(x => x.BlockedUserId)
            .NotEmpty();
    }
}
