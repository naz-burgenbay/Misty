using MediatR;

namespace Misty.Application.Users;

public record LoginCommand(string Username, string Password) : IRequest<LoginResponse>;

public record LoginResponse(string AccessToken, string RefreshToken, Guid UserId);
