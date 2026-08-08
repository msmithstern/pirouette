using Microsoft.EntityFrameworkCore;
using Pirouette.Domain;

namespace Pirouette.Integration.Tests;

/// <summary>
/// Proves that the global query filters and the stamping interceptor actually isolate one
/// studio's data from another's.
/// </summary>
/// <remarks>
/// This is the test the whole tenancy design rests on. Nothing in the database schema enforces
/// tenant isolation — the migration contains no trace of it — so the guarantee is entirely an
/// application-level promise. These tests are the only thing that checks the promise is kept.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class TenantIsolationTests(PostgresFixture fixture)
{
    /// <summary>
    /// Creates two studios and returns their ids. Fresh ids per test, so tests sharing the
    /// container cannot see each other's rows.
    /// </summary>
    private async Task<(Guid StudioA, Guid StudioB)> CreateTwoStudiosAsync()
    {
        var studioA = new Studio("Studio A", "America/New_York");
        var studioB = new Studio("Studio B", "America/Los_Angeles");

        // Studio is not tenant-owned, so it is unfiltered and any context can write both.
        await using var context = fixture.CreateContext(studioA.Id);
        context.Studios.AddRange(studioA, studioB);
        await context.SaveChangesAsync();

        return (studioA.Id, studioB.Id);
    }

    [Fact]
    public async Task Rooms_AreNotVisibleToAnotherStudio()
    {
        var (studioA, studioB) = await CreateTwoStudiosAsync();

        await using (var contextA = fixture.CreateContext(studioA))
        {
            contextA.Rooms.Add(new Room("A-Main", 20));
            await contextA.SaveChangesAsync();
        }

        await using (var contextB = fixture.CreateContext(studioB))
        {
            contextB.Rooms.Add(new Room("B-Main", 30));
            await contextB.SaveChangesAsync();
        }

        await using var query = fixture.CreateContext(studioA);
        var rooms = await query.Rooms.ToListAsync();

        Assert.Single(rooms);
        Assert.Equal("A-Main", rooms[0].Name);
    }

    [Fact]
    public async Task Terms_AreNotVisibleToAnotherStudio()
    {
        // Deliberately a separate test from Rooms rather than a nicety. The query filters are
        // applied by reflection over every ITenantOwned type; an earlier version of the
        // DbContext filtered Room explicitly and silently left Term wide open. Only a test
        // that exercises a *second* entity catches that class of mistake.
        var (studioA, studioB) = await CreateTwoStudiosAsync();

        await using (var contextA = fixture.CreateContext(studioA))
        {
            contextA.Terms.Add(new Term("Fall 2026", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 15)));
            await contextA.SaveChangesAsync();
        }

        await using (var contextB = fixture.CreateContext(studioB))
        {
            contextB.Terms.Add(new Term("Spring 2027", new DateOnly(2027, 1, 5), new DateOnly(2027, 5, 20)));
            await contextB.SaveChangesAsync();
        }

        await using var query = fixture.CreateContext(studioA);
        var terms = await query.Terms.ToListAsync();

        Assert.Single(terms);
        Assert.Equal("Fall 2026", terms[0].Name);
    }

    [Fact]
    public async Task IgnoreQueryFilters_RevealsTheOtherStudiosRows()
    {
        // The inverse of the tests above, and the reason they mean anything. Without this, a
        // passing isolation test is equally consistent with the write having silently failed.
        // This proves both rows are really in the table and the filter is what hides one.
        var (studioA, studioB) = await CreateTwoStudiosAsync();

        await using (var contextA = fixture.CreateContext(studioA))
        {
            contextA.Rooms.Add(new Room("Visible-A", 10));
            await contextA.SaveChangesAsync();
        }

        await using (var contextB = fixture.CreateContext(studioB))
        {
            contextB.Rooms.Add(new Room("Visible-B", 10));
            await contextB.SaveChangesAsync();
        }

        await using var query = fixture.CreateContext(studioA);

        var filtered = await query.Rooms
            .Where(r => r.Name.StartsWith("Visible-"))
            .ToListAsync();

        var unfiltered = await query.Rooms
            .IgnoreQueryFilters()
            .Where(r => r.Name.StartsWith("Visible-"))
            .ToListAsync();

        Assert.Single(filtered);
        Assert.Equal(2, unfiltered.Count);
    }

    [Fact]
    public async Task Insert_StampsTheCurrentStudioId()
    {
        var (studioA, _) = await CreateTwoStudiosAsync();

        var room = new Room("Unstamped", 15);
        Assert.Equal(Guid.Empty, room.StudioId);

        await using var context = fixture.CreateContext(studioA);
        context.Rooms.Add(room);
        await context.SaveChangesAsync();

        // Query filters are read-only protection; nothing about them prevents writing a row
        // with the wrong tenant or none at all. The interceptor is the write-side half.
        Assert.Equal(studioA, room.StudioId);
    }

    [Fact]
    public async Task Insert_DoesNotOverwriteAnExplicitStudioId()
    {
        var (studioA, studioB) = await CreateTwoStudiosAsync();

        // Set deliberately to the *other* studio. The interceptor only fills in blanks, so this
        // value survives — which is what makes seeding and admin tooling possible. Worth knowing
        // it is a deliberate hole: writing cross-tenant data is possible if you try.
        var room = new Room("Explicit", 12) { StudioId = studioB };

        await using (var context = fixture.CreateContext(studioA))
        {
            context.Rooms.Add(room);
            await context.SaveChangesAsync();
        }

        Assert.Equal(studioB, room.StudioId);

        await using var queryB = fixture.CreateContext(studioB);
        Assert.Contains(await queryB.Rooms.ToListAsync(), r => r.Name == "Explicit");
    }

    [Fact]
    public async Task SeededStudio_ExistsAndIsUnfiltered()
    {
        // Studio has no query filter, so it is visible regardless of the current tenant. This
        // is what makes tenant resolution possible at all: looking a studio up cannot require
        // already knowing which studio you are.
        await using var context = fixture.CreateContext(Guid.NewGuid());

        var seeded = await context.Studios.FindAsync(
            Infrastructure.Configurations.StudioConfiguration.SeedStudioId);

        Assert.NotNull(seeded);
        Assert.Equal("Pirouette Dance Academy", seeded!.Name);
    }
}
