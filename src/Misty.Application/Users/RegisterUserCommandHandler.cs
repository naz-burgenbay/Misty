using MediatR;
using Microsoft.AspNetCore.Identity;
using Misty.Application.Common.Exceptions;
using Misty.Domain.Users;

namespace Misty.Application.Users;

public sealed class RegisterUserCommandHandler : IRequestHandler<RegisterUserCommand, RegisterUserResponse>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher<User> _hasher;

    public RegisterUserCommandHandler(IUserRepository users, IPasswordHasher<User> hasher)
    {
        _users = users;
        _hasher = hasher;
    }

    public async Task<RegisterUserResponse> Handle(RegisterUserCommand cmd, CancellationToken ct)
    {
        if (await _users.UsernameExistsAsync(cmd.Username, ct))
            throw new ConflictException($"Username '{cmd.Username}' is already taken.");

        var user = User.Create(Guid.NewGuid(), cmd.Username, cmd.DisplayName);
        user.SetPasswordHash(_hasher.HashPassword(user, cmd.Password));

        await _users.AddAsync(user, ct);

        return new RegisterUserResponse(user.Id);
    }
}
