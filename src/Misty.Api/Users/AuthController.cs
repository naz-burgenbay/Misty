using MediatR;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Users;

namespace Misty.Api.Users;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator) => _mediator = mediator;

    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(RegisterUserRequest request, CancellationToken ct)
    {
        var response = await _mediator.Send(
            new RegisterUserCommand(request.Username, request.DisplayName, request.Password),
            ct);

        return Created($"/api/v1/users/{response.UserId}", new { userId = response.UserId });
    }
}

public record RegisterUserRequest(string Username, string DisplayName, string Password);
