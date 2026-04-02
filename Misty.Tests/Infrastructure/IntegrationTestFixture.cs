using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Misty.Infrastructure;
using Misty.Infrastructure.Identity;
using Respawn;
using Testcontainers.MsSql;

namespace Misty.Tests.Infrastructure;

// Uses Respawn to reset data between tests (faster than re-creating the DB).
public class IntegrationTestFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private Respawner _respawner = null!;
    private string _connectionString = null!;

    public string ConnectionString => _connectionString;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        // Create schema via EF migrations
        await using var db = CreateDbContext();
        await db.Database.EnsureCreatedAsync();

        _respawner = await Respawner.CreateAsync(_connectionString, new RespawnerOptions
        {
            TablesToIgnore = ["__EFMigrationsHistory"],
            DbAdapter = DbAdapter.SqlServer
        });
    }

    public async Task ResetAsync()
    {
        await _respawner.ResetAsync(_connectionString);
    }

    public ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }

    // Creates a UserManager backed by the real database. Used when tests need Identity operations like account deletion.
    public UserManager<ApplicationUser> CreateUserManager()
    {
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(_connectionString));
        services.AddIdentityCore<ApplicationUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<UserManager<ApplicationUser>>();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    public const string Name = "Integration";
}
