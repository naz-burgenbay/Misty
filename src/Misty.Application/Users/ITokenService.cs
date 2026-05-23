using Misty.Domain.Users;

namespace Misty.Application.Users;

public interface ITokenService
{
    string CreateAccessToken(User user);
    (string Token, string TokenHash, DateTime ExpiresAt) CreateRefreshToken();
}
