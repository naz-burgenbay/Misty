using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Communication;
using System.IdentityModel.Tokens.Jwt;

namespace Misty.Api.Users;

[ApiController]
[Route("api/v1/users/{id:guid}/block")]
[Authorize]
public sealed class UserBlockController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserBlockController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Block(Guid id, CancellationToken ct)
    {
        var callerId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new BlockUserCommand(callerId, id), ct);
        return NoContent();
    }

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken ct)
    {
        var callerId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        await _mediator.Send(new UnblockUserCommand(callerId, id), ct);
        return NoContent();
    }
}
