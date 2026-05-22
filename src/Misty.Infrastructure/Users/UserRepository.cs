using Microsoft.EntityFrameworkCore;
using Misty.Application.Users;
using Misty.Domain.Users;
using Misty.Infrastructure.Persistence;

namespace Misty.Infrastructure.Users;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _db;

    public UserRepository(ApplicationDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Users
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted, ct);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => _db.Users
            .FirstOrDefaultAsync(u => u.Username == username && !u.IsDeleted, ct);

    public Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
        => _db.Users.AnyAsync(u => u.Username == username, ct);

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await _db.Users.AddAsync(user, ct);
        await _db.SaveChangesAsync(ct);
    }
}
