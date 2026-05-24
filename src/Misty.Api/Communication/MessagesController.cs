using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Messaging;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/channels/{channelId:guid}/messages")]
[Authorize]
public sealed class MessagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public MessagesController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    [ProducesResponseType(typeof(SendMessageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SendMessage(
        Guid channelId,
        SendChannelMessageRequest request,
        CancellationToken ct)
    {
        var authorId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(
            new SendChannelMessageCommand(
                channelId,
                authorId,
                request.Content,
                request.IdempotencyKey,
                request.ParentMessageId),
            ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}

public record SendChannelMessageRequest(
    string Content,
    string IdempotencyKey,
    Guid? ParentMessageId);
