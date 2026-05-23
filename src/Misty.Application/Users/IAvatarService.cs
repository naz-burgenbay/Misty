namespace Misty.Application.Users;

public interface IAvatarService
{
    Task<string> UploadAsync(Guid userId, Stream content, string contentType, CancellationToken ct = default);
}
