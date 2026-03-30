namespace Misty.Application.Interfaces;

public interface IIdentityService
{
    Task<string?> GetEmailAsync(string userId, CancellationToken ct = default);
    Task AnonymizeIdentityAsync(string userId, CancellationToken ct = default);
}
