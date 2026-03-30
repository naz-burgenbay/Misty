using FluentValidation;
using Misty.Application.DTOs.Channels;

namespace Misty.Application.Validation.Channels;

public class MarkChannelReadRequestValidator : AbstractValidator<MarkChannelReadRequest>
{
    public MarkChannelReadRequestValidator()
    {
        RuleFor(x => x.LastReadAt)
            .NotEmpty();
    }
}
