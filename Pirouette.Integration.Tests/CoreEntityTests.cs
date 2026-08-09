using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pirouette.Domain;

namespace Pirouette.Integration.Tests;

/// <summary>
/// Covers the two things about the M1 entities that unit tests structurally cannot: that the
/// aggregates survive a round trip through real SQL, and that the <c>CHECK</c> constraints
/// behind the domain guards actually fire.
/// </summary>
/// <remarks>
/// The constraint tests deliberately go around the domain layer with raw SQL. That is the
/// entire point of having them: the guard clauses are the real enforcement, and the constraints
/// exist for the paths that never touch C# — migrations, bulk imports, someone in psql. A test
/// that went through the constructor would only prove the constructor works, which the domain
/// tests already do, and would leave the second layer unverified.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public class CoreEntityTests(PostgresFixture fixture)
{
    private async Task<Guid> CreateStudioAsync()
    {
        var studio = new Studio($"Studio {Guid.NewGuid():N}", "America/New_York");

        await using var context = fixture.CreateContext(studio.Id);
        context.Studios.Add(studio);
        await context.SaveChangesAsync();

        return studio.Id;
    }

    // --- Round trips ---------------------------------------------------------------------

    [Fact]
    public async Task AMemberAndItsRoles_RoundTrip()
    {
        var studioId = await CreateStudioAsync();

        var nadia = new Member("Nadia", "Okonkwo", new DateOnly(2010, 3, 14));
        nadia.GrantRole(StudioRole.Student, new DateOnly(2024, 9, 1));
        nadia.GrantRole(StudioRole.AssistantStaff, new DateOnly(2026, 9, 1));

        await using (var write = fixture.CreateContext(studioId))
        {
            write.Members.Add(nadia);
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext(studioId);

        var reloaded = await read.Members
            .Include(m => m.Roles)
            .SingleAsync(m => m.Id == nadia.Id);

        // The roles come back through a private backing field behind a read-only property. If
        // the field access mode were not configured, materialisation would throw here rather
        // than return an empty collection — which is the useful failure, since a silently
        // empty collection would make every overlap check pass.
        Assert.Equal(2, reloaded.Roles.Count);
        Assert.True(reloaded.IsStaffOn(new DateOnly(2026, 10, 1)));
        Assert.True(reloaded.HasRole(StudioRole.Student, new DateOnly(2026, 10, 1)));
        Assert.Equal(16, reloaded.AgeOn(new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public async Task AClassWithPatternsAndAssignments_RoundTrips()
    {
        var studioId = await CreateStudioAsync();

        await using (var write = fixture.CreateContext(studioId))
        {
            var term = new Term("Fall 2026", new DateOnly(2026, 9, 8), new DateOnly(2026, 12, 19))
            {
                StudioId = studioId
            };

            var room = new Room("Studio A", 20) { StudioId = studioId };
            var instructor = new Member("Mei", "Chen") { StudioId = studioId };

            var danceClass = new DanceClass(term, "Intermediate Ballet", "Ballet",
                capacity: 14, room: room, level: "Level 3", minimumAge: 11, maximumAge: 15);

            danceClass.AddMeetingPattern(DayOfWeek.Tuesday, new TimeOnly(17, 0), TimeSpan.FromMinutes(90));
            danceClass.AddMeetingPattern(DayOfWeek.Thursday, new TimeOnly(17, 0), TimeSpan.FromMinutes(90));
            danceClass.Assign(instructor, AssignmentRole.Lead);

            write.Terms.Add(term);
            write.Rooms.Add(room);
            write.Members.Add(instructor);
            write.DanceClasses.Add(danceClass);

            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext(studioId);

        var reloaded = await read.DanceClasses
            .Include(c => c.MeetingPatterns)
            .Include(c => c.Assignments)
            .SingleAsync();

        Assert.Equal(2, reloaded.MeetingPatterns.Count);
        Assert.Single(reloaded.Assignments);

        // TimeOnly and TimeSpan map to Postgres `time` and `interval` with no converter. Worth
        // asserting rather than assuming: a duration silently round-tripping through a wrong
        // type would put every generated session at the wrong length.
        var tuesday = reloaded.MeetingPatterns.Single(p => p.DayOfWeek == DayOfWeek.Tuesday);
        Assert.Equal(new TimeOnly(17, 0), tuesday.StartTime);
        Assert.Equal(TimeSpan.FromMinutes(90), tuesday.Duration);
        Assert.Equal(new TimeOnly(18, 30), tuesday.EndTime);
    }

    [Fact]
    public async Task AClassWithNoMeetingPattern_IsPersistable()
    {
        // The workshop shape. Nothing in the schema requires a pattern, and nothing should.
        var studioId = await CreateStudioAsync();

        await using (var write = fixture.CreateContext(studioId))
        {
            var term = new Term("Fall 2026", new DateOnly(2026, 9, 8), new DateOnly(2026, 12, 19))
            {
                StudioId = studioId
            };

            write.Terms.Add(term);
            write.DanceClasses.Add(new DanceClass(term, "Nutcracker Rehearsals", "Ballet", capacity: 30));
            await write.SaveChangesAsync();
        }

        await using var read = fixture.CreateContext(studioId);

        Assert.Empty((await read.DanceClasses.Include(c => c.MeetingPatterns).SingleAsync())
            .MeetingPatterns);
    }

    // --- Tenant isolation of the child entities -------------------------------------------

    [Fact]
    public async Task MemberRoles_AreNotVisibleToAnotherStudio()
    {
        // Roles have no DbSet, so it would be easy to assume they are covered by the member's
        // filter. They are not — they get their own, from the same reflection pass over
        // ITenantOwned — and this asserts the pass reached an entity that is only ever reached
        // through a navigation.
        var studioA = await CreateStudioAsync();
        var studioB = await CreateStudioAsync();

        await using (var contextA = fixture.CreateContext(studioA))
        {
            var member = new Member("Mei", "Chen");
            member.GrantRole(StudioRole.Owner, new DateOnly(2020, 1, 1));
            contextA.Members.Add(member);
            await contextA.SaveChangesAsync();
        }

        await using var contextB = fixture.CreateContext(studioB);

        Assert.Empty(await contextB.Set<MemberRole>().ToListAsync());

        // Scoped to studio A rather than counting every role in the table. The container is
        // shared across the whole test run, so an unscoped count would be a function of how
        // many other tests had already run — green today, red the moment one is added.
        Assert.Equal(1, await contextB.Set<MemberRole>()
            .IgnoreQueryFilters()
            .CountAsync(r => r.StudioId == studioA));
    }

    // --- Storage shapes --------------------------------------------------------------------

    [Fact]
    public async Task RolesAreStoredAsText_NotAsEnumOrdinals()
    {
        // If this were stored as an integer, reordering StudioRole would silently reassign
        // everyone's permissions: no compiler error, no failing test, and no visible symptom
        // until someone saw data they should not.
        var studioId = await CreateStudioAsync();

        await using var context = fixture.CreateContext(studioId);

        var member = new Member("Nadia", "Okonkwo");
        member.GrantRole(StudioRole.AssistantStaff, new DateOnly(2026, 9, 1));
        context.Members.Add(member);
        await context.SaveChangesAsync();

        // SqlQueryRaw requires the column to be aliased "Value" when projecting to a scalar.
        var stored = await context.Database
            .SqlQueryRaw<string>(
                """SELECT "Role" AS "Value" FROM "MemberRoles" WHERE "MemberId" = @p0""",
                member.Id)
            .SingleAsync();

        Assert.Equal("AssistantStaff", stored);
    }

    // --- CHECK constraints -------------------------------------------------------------

    [Fact]
    public async Task CheckConstraint_RejectsABackwardsTerm()
    {
        var studioId = await CreateStudioAsync();

        await using var context = fixture.CreateContext(studioId);

        var insert = await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Terms" ("Id", "StudioId", "Name", "StartDate", "EndDate")
            VALUES (@p0, @p1, 'Backwards', DATE '2026-12-15', DATE '2026-09-01')
            """,
            Guid.CreateVersion7(), studioId));

        AssertCheckConstraintViolation(insert, "CK_Term_EndNotBeforeStart");
    }

    [Fact]
    public async Task CheckConstraint_RejectsAZeroCapacityRoom()
    {
        var studioId = await CreateStudioAsync();

        await using var context = fixture.CreateContext(studioId);

        var insert = await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Rooms" ("Id", "StudioId", "Name", "Capacity")
            VALUES (@p0, @p1, 'Broom cupboard', 0)
            """,
            Guid.CreateVersion7(), studioId));

        AssertCheckConstraintViolation(insert, "CK_Room_CapacityPositive");
    }

    [Fact]
    public async Task CheckConstraint_RejectsAPatternThatCrossesMidnight()
    {
        // Updated rather than inserted, so the row exists with every foreign key satisfied and
        // the only thing wrong with it is the thing under test.
        var studioId = await CreateStudioAsync();

        await using var context = fixture.CreateContext(studioId);

        var term = new Term("Fall 2026", new DateOnly(2026, 9, 8), new DateOnly(2026, 12, 19))
        {
            StudioId = studioId
        };

        var danceClass = new DanceClass(term, "Late Night Tap", "Tap", capacity: 10);
        var pattern = danceClass.AddMeetingPattern(DayOfWeek.Friday, new TimeOnly(22, 0), TimeSpan.FromHours(1));

        context.Terms.Add(term);
        context.DanceClasses.Add(danceClass);
        await context.SaveChangesAsync();

        var update = await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync(
            """
            UPDATE "MeetingPatterns" SET "Duration" = INTERVAL '3 hours' WHERE "Id" = @p0
            """,
            pattern.Id));

        AssertCheckConstraintViolation(update, "CK_MeetingPattern_DoesNotCrossMidnight");
    }

    [Fact]
    public async Task CheckConstraint_RejectsAnImplausiblyLongClass()
    {
        var studioId = await CreateStudioAsync();

        await using var context = fixture.CreateContext(studioId);

        var term = new Term("Fall 2026", new DateOnly(2026, 9, 8), new DateOnly(2026, 12, 19))
        {
            StudioId = studioId
        };

        var danceClass = new DanceClass(term, "Morning Ballet", "Ballet", capacity: 10);
        var pattern = danceClass.AddMeetingPattern(DayOfWeek.Monday, new TimeOnly(9, 0), TimeSpan.FromHours(1));

        context.Terms.Add(term);
        context.DanceClasses.Add(danceClass);
        await context.SaveChangesAsync();

        // Nine hours starting at 09:00 ends at 18:00, so this does not cross midnight — it can
        // only trip the duration bound. Chosen that way on purpose: a value that violated both
        // constraints would pass this test even if the duration check had been dropped.
        var update = await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync(
            """
            UPDATE "MeetingPatterns" SET "Duration" = INTERVAL '9 hours' WHERE "Id" = @p0
            """,
            pattern.Id));

        AssertCheckConstraintViolation(update, "CK_MeetingPattern_DurationWithinLimit");
    }

    [Fact]
    public async Task CheckConstraint_RejectsARoleEndingBeforeItBegins()
    {
        var studioId = await CreateStudioAsync();

        await using var context = fixture.CreateContext(studioId);

        var member = new Member("Mei", "Chen");
        var role = member.GrantRole(StudioRole.Staff, new DateOnly(2026, 9, 1));
        context.Members.Add(member);
        await context.SaveChangesAsync();

        var update = await Record.ExceptionAsync(() => context.Database.ExecuteSqlRawAsync(
            """
            UPDATE "MemberRoles" SET "EffectiveUntil" = DATE '2024-01-01' WHERE "Id" = @p0
            """,
            role.Id));

        AssertCheckConstraintViolation(update, "CK_MemberRole_UntilNotBeforeFrom");
    }

    [Fact]
    public async Task CheckConstraint_AllowsAnOpenEndedRole()
    {
        // The constraint has to tolerate NULL, or it would reject the ordinary case of a
        // current student. Asserting the negative is what stops a stricter-looking rewrite of
        // the check from quietly breaking every insert.
        var studioId = await CreateStudioAsync();

        await using var context = fixture.CreateContext(studioId);

        var member = new Member("Mei", "Chen");
        member.GrantRole(StudioRole.Staff, new DateOnly(2026, 9, 1));
        context.Members.Add(member);

        await context.SaveChangesAsync();

        Assert.Equal(1, await context.Set<MemberRole>().CountAsync());
    }

    // --- Helpers -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that <paramref name="exception"/> is a Postgres check violation naming
    /// <paramref name="constraintName"/>.
    /// </summary>
    /// <remarks>
    /// Matched on SQLSTATE and the constraint name rather than on message text, and rather than
    /// on "some exception was thrown". A typo in the test's own SQL also throws, and would make
    /// a looser assertion pass while proving nothing — which is the failure these tests are
    /// most likely to contain, since every one of them is hand-written SQL.
    /// </remarks>
    private static void AssertCheckConstraintViolation(Exception? exception, string constraintName)
    {
        Assert.NotNull(exception);

        // Walked rather than assumed. Npgsql surfaces the error directly for raw SQL, but EF
        // wraps it during SaveChanges, and a helper that only understood one of those shapes
        // would fail confusingly the first time it was reused.
        var postgres = Unwrap(exception);

        Assert.NotNull(postgres);
        Assert.Equal("23514", postgres!.SqlState); // check_violation
        Assert.Equal(constraintName, postgres.ConstraintName);

        static PostgresException? Unwrap(Exception? exception) => exception switch
        {
            null => null,
            PostgresException postgres => postgres,
            _ => Unwrap(exception.InnerException)
        };
    }
}
