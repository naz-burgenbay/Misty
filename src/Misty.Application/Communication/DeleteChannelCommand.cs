using MediatR;
using Misty.Application.Common.Exceptions;

namespace Misty.Application.Communication;

public record DeleteChannelCommand(Guid ChannelId) : IRequest;

public sealed class DeleteChannelCommandHandler : IRequestHandler<DeleteChannelCommand>
{
    private readonly IChannelRepository _channels;

    public DeleteChannelCommandHandler(IChannelRepository channels) => _channels = channels;

    public async Task Handle(DeleteChannelCommand request, CancellationToken ct)
    {
        var channel = await _channels.GetByIdAsync(request.ChannelId, ct)
            ?? throw new NotFoundException($"Channel '{request.ChannelId}' was not found.");

        await _channels.SoftDeleteAsync(channel, ct);
    }
}
