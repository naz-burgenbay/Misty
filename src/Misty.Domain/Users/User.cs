namespace Misty.Domain.Users;

public class User
{
    private User() { } // For EF Core

    public Guid Id { get; private set; }
    public string Username { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? Bio { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public byte[] Version { get; private set; } = null!;

    public static User Create(Guid id, string username, string displayName, string passwordHash)
        => new()
        {
            Id = id,
            Username = username,
            DisplayName = displayName,
            PasswordHash = passwordHash,
        };
}
