using MediatR;
using Misty.Application.Common.Exceptions;

namespace Misty.Application.Users;

public record RemoveAvatarCommand(Guid UserId) : IRequest<RemoveAvatarResponse>;

public record RemoveAvatarResponse(string Version);

public sealed class RemoveAvatarCommandHandler : IRequestHandler<RemoveAvatarCommand, RemoveAvatarResponse>
{
    private readonly IAvatarService _avatar;
    private readonly IUserRepository _users;

    public RemoveAvatarCommandHandler(IAvatarService avatar, IUserRepository users)
    {
        _avatar = avatar;
        _users = users;
    }

    public async Task<RemoveAvatarResponse> Handle(RemoveAvatarCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct)
            ?? throw new UnauthorizedException();

        await _avatar.DeleteAsync(request.UserId, ct);
        await _users.UpdateAvatarUrlAsync(user, null, ct);

        return new RemoveAvatarResponse(Convert.ToBase64String(user.Version));
    }
}
