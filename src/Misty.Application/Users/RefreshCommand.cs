using MediatR;

namespace Misty.Application.Users;

public record RefreshCommand(string RefreshToken) : IRequest<RefreshResponse>;
public record RefreshResponse(string AccessToken, string RefreshToken);
