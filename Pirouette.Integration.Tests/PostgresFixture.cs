using Microsoft.EntityFrameworkCore;
using Pirouette.Infrastructure;
using Testcontainers.PostgreSql;

namespace Pirouette.Integration.Tests;

/// <summary>
/// Starts a throwaway PostgreSQL container for the test run and applies migrations to it.
/// </summary>
/// <remarks>
/// A real database rather than the in-memory provider, because the things under test here —
/// global query filters, foreign keys, generated SQL — either do not exist in the in-memory
/// provider or behave differently there. A test that passes against a fake and fails against
/// Postgres is worse than no test.
///
/// <para>The container is created once for the whole collection and torn down afterwards.
/// Starting one per test would be correct but far too slow; instead each test creates its own
/// studios with fresh ids, so tests cannot see each other's rows.</para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    // Pinned to the same major version as docker-compose.yml. Testing against a different
    // version from the one you develop on defeats most of the point.
    //
    // The image is a constructor argument because Testcontainers 4.10 obsoleted the
    // parameterless builders: modules no longer ship a default image version, so that projects
    // cannot silently inherit an outdated or vulnerable one. Passing it explicitly is now the
    // only option, which is what the best practices always recommended anyway.
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Applies InitialCreate, so the schema under test is the one the migration produces
        // rather than one EnsureCreated invented. If a migration is broken, these tests fail —
        // which is the correct outcome.
        await using var context = CreateContext(Guid.Empty);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Builds a context that behaves as though a request from <paramref name="studioId"/> were
    /// in flight — same query filter, same stamping interceptor as the running application.
    /// </summary>
    public PirouetteDbContext CreateContext(Guid studioId)
    {
        var tenantProvider = new FixedTenantProvider(studioId);

        var options = new DbContextOptionsBuilder<PirouetteDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .AddInterceptors(new TenantStampingInterceptor(tenantProvider))
            .Options;

        return new PirouetteDbContext(options, tenantProvider);
    }
}

/// <summary>
/// Shares one container across every test class marked with this collection.
/// </summary>
[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
