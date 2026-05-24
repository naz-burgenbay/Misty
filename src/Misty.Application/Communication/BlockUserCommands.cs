using FluentValidation;
using MediatR;
using Misty.Application.Communication.Contracts;

namespace Misty.Application.Communication;

public record BlockUserCommand(Guid BlockerId, Guid BlockedId) : IRequest;

public sealed class BlockUserValidator : AbstractValidator<BlockUserCommand>
{
    public BlockUserValidator()
    {
        RuleFor(x => x.BlockedId)
            .NotEqual(x => x.BlockerId)
            .WithMessage("Cannot block yourself.");
    }
}

public sealed class BlockUserCommandHandler : IRequestHandler<BlockUserCommand>
{
    private readonly IUserBlockService _svc;
    public BlockUserCommandHandler(IUserBlockService svc) => _svc = svc;
    public Task Handle(BlockUserCommand request, CancellationToken ct)
        => _svc.BlockAsync(request.BlockerId, request.BlockedId, ct);
}

public record UnblockUserCommand(Guid BlockerId, Guid BlockedId) : IRequest;

public sealed class UnblockUserCommandHandler : IRequestHandler<UnblockUserCommand>
{
    private readonly IUserBlockService _svc;
    public UnblockUserCommandHandler(IUserBlockService svc) => _svc = svc;
    public Task Handle(UnblockUserCommand request, CancellationToken ct)
        => _svc.UnblockAsync(request.BlockerId, request.BlockedId, ct);
}
