using FluentValidation;
using Misty.Application.DTOs.Channels;

namespace Misty.Application.Validation.Channels;

public class UpdateChannelMemberRolesRequestValidator : AbstractValidator<UpdateChannelMemberRolesRequest>
{
    public UpdateChannelMemberRolesRequestValidator()
    {
        RuleFor(x => x.RoleIds)
            .NotNull();
    }
}
