using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Misty.Application.Messaging;

namespace Misty.Api.Communication;

[ApiController]
[Route("api/v1/conversations/{conversationId:guid}/messages")]
[Authorize]
public sealed class ConversationMessagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public ConversationMessagesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    [ProducesResponseType(typeof(GetChannelMessagesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMessages(
        Guid conversationId,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(new GetConversationMessagesQuery(conversationId, userId, pageSize, cursor), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SendMessageResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SendMessage(
        Guid conversationId,
        SendConversationMessageRequest request,
        CancellationToken ct)
    {
        var authorId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
        var result = await _mediator.Send(
            new SendConversationMessageCommand(
                conversationId,
                authorId,
                request.Content,
                request.IdempotencyKey,
                request.ParentMessageId),
            ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }
}

public record SendConversationMessageRequest(
    string Content,
    string IdempotencyKey,
    Guid? ParentMessageId);
