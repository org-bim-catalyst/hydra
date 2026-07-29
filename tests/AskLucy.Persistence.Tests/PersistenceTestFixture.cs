using System.Threading.Tasks;
using AskLucy.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace AskLucy.Persistence.Tests;

/// <summary>
/// Spins up a real SQL Server instance via Testcontainers (constitution &#167;10: integration
/// tests run against a real/test SQL Server instance, not a fake provider) and applies every
/// migration once, so tests exercise the actual global query filters/indexes/constraints EF
/// Core generates — not an in-memory approximation of them.
/// </summary>
public sealed class PersistenceTestFixture : IAsyncLifetime
{
    private readonly MsSqlBuilder _builder = new("mcr.microsoft.com/mssql/server:2022-latest");
    private MsSqlContainer? _container;

    public async ValueTask InitializeAsync()
    {
        _container = _builder.Build();
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public AskLucyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AskLucyDbContext>()
            .UseSqlServer(_container!.GetConnectionString())
            .Options;

        return new AskLucyDbContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

// CA1711 flags the "Collection" suffix, but this is xUnit's own required naming pattern
// for a collection-fixture definition, not an ambiguous System.Collections-style name.
#pragma warning disable CA1711
[CollectionDefinition(Name)]
public sealed class PersistenceTestCollection : ICollectionFixture<PersistenceTestFixture>
{
    public const string Name = "Persistence";
}
#pragma warning restore CA1711
