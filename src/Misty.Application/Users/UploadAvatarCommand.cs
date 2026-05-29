using MediatR;
using Misty.Application.Common.Exceptions;

namespace Misty.Application.Users;

public record UploadAvatarCommand(Guid UserId, Stream Content, string ContentType)
    : IRequest<UploadAvatarResponse>;

public record UploadAvatarResponse(string AvatarUrl, string Version);

public sealed class UploadAvatarCommandHandler : IRequestHandler<UploadAvatarCommand, UploadAvatarResponse>
{
    private readonly IAvatarService _avatar;
    private readonly IUserRepository _users;

    public UploadAvatarCommandHandler(IAvatarService avatar, IUserRepository users)
    {
        _avatar = avatar;
        _users = users;
    }

    public async Task<UploadAvatarResponse> Handle(UploadAvatarCommand request, CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(request.UserId, ct)
            ?? throw new UnauthorizedException();

        var url = await _avatar.UploadAsync(request.UserId, request.Content, request.ContentType, ct);
        await _users.UpdateAvatarUrlAsync(user, url, ct);

        return new UploadAvatarResponse(url, Convert.ToBase64String(user.Version));
    }
}
