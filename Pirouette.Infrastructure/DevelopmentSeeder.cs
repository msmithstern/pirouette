using Microsoft.EntityFrameworkCore;
using Pirouette.Domain;

namespace Pirouette.Infrastructure;

/// <summary>
/// Populates an empty database with one realistic studio's worth of data.
/// </summary>
/// <remarks>
/// A runtime seeder rather than <c>HasData</c> in a migration. Every entity here generates its
/// own id in its constructor, so <c>HasData</c> — which needs literal, stable key values known
/// at design time — would mean either bypassing the constructors with anonymous objects, as
/// <see cref="Configurations.StudioConfiguration"/> is forced to do for the tenant row, or
/// inventing a second construction path purely for seeding. Both defeat the point: running the
/// seed through the real constructors means the seed data is itself a check that the
/// invariants admit realistic input.
///
/// <para>Development only, and idempotent — it does nothing if any member already exists, so
/// restarting the app does not duplicate the studio.</para>
///
/// <para>The seed deliberately includes a student-teacher, a class with no meeting pattern, a
/// one-week substitute, and two siblings in one household. Those are the four cases the model
/// was shaped around, and a seed that omitted them would let a regression in any of them go
/// unnoticed while the app still looked fine.</para>
/// </remarks>
public static class DevelopmentSeeder
{
    public static async Task SeedAsync(PirouetteDbContext db, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (await db.Members.AnyAsync(cancellationToken))
        {
            return;
        }

        var studioId = db.StudioId;

        // --- Term and rooms -------------------------------------------------------------

        var fall = new Term("Fall 2026", new DateOnly(2026, 9, 8), new DateOnly(2026, 12, 19))
        {
            StudioId = studioId
        };

        var studioA = new Room("Studio A", 24) { StudioId = studioId };
        var studioB = new Room("Studio B", 16) { StudioId = studioId };
        var studioC = new Room("Studio C", 12) { StudioId = studioId };

        db.Terms.Add(fall);
        db.Rooms.AddRange(studioA, studioB, studioC);

        // --- Households -----------------------------------------------------------------

        var okonkwo = new Household("The Okonkwo Family", "42 Rosewood Avenue", "Brookline", "MA",
            "02445", "617-555-0142", "okonkwo.family@example.com") { StudioId = studioId };

        var alvarez = new Household("The Alvarez Family", "9 Elm Street", "Newton", "MA",
            "02458", "617-555-0198", "alvarez.home@example.com") { StudioId = studioId };

        db.Households.AddRange(okonkwo, alvarez);

        // --- Staff ----------------------------------------------------------------------

        // No date of birth on the staff members. It is not the studio's business, and leaving
        // it null exercises the optional-DOB path rather than pretending every row is complete.
        var mei = new Member("Mei", "Chen", email: "mei@pirouette.example") { StudioId = studioId };
        mei.GrantRole(StudioRole.Owner, new DateOnly(2018, 1, 1));
        mei.GrantRole(StudioRole.Staff, new DateOnly(2018, 1, 1));

        var luis = new Member("Luis", "Ramos", email: "luis@pirouette.example") { StudioId = studioId };
        luis.GrantRole(StudioRole.Staff, new DateOnly(2022, 8, 15));

        var priya = new Member("Priya", "Nair", email: "priya@pirouette.example") { StudioId = studioId };
        priya.GrantRole(StudioRole.Staff, new DateOnly(2024, 1, 8));

        // --- The student-teacher --------------------------------------------------------

        // The case the whole model is shaped around. One human, two roles, both dated: she has
        // been a student since 2024 and started assisting in September 2026. Modelled as two
        // entities she would be two rows with two birthdays to keep in sync, and no way to
        // answer whether they are the same person.
        var nadia = new Member("Nadia", "Okonkwo", new DateOnly(2010, 3, 14),
            email: "nadia.o@example.com", household: okonkwo) { StudioId = studioId };
        nadia.GrantRole(StudioRole.Student, new DateOnly(2024, 9, 1));
        nadia.GrantRole(StudioRole.AssistantStaff, new DateOnly(2026, 9, 1));

        // --- Students and guardians -----------------------------------------------------

        var amara = new Member("Amara", "Okonkwo", new DateOnly(2019, 5, 2), household: okonkwo)
        {
            StudioId = studioId
        };
        amara.GrantRole(StudioRole.Student, new DateOnly(2025, 9, 1));

        var chidi = new Member("Chidi", "Okonkwo", email: "chidi.o@example.com",
            phone: "617-555-0142", household: okonkwo) { StudioId = studioId };
        chidi.GrantRole(StudioRole.Guardian, new DateOnly(2024, 9, 1));

        var sofia = new Member("Sofia", "Alvarez", new DateOnly(2013, 11, 20), household: alvarez)
        {
            StudioId = studioId
        };
        sofia.GrantRole(StudioRole.Student, new DateOnly(2023, 9, 1));

        var diego = new Member("Diego", "Alvarez", new DateOnly(2017, 7, 8), household: alvarez)
        {
            StudioId = studioId
        };
        diego.GrantRole(StudioRole.Student, new DateOnly(2026, 9, 1));

        var elena = new Member("Elena", "Alvarez", email: "elena.a@example.com",
            phone: "617-555-0198", household: alvarez) { StudioId = studioId };
        elena.GrantRole(StudioRole.Guardian, new DateOnly(2023, 9, 1));

        // An adult with no household: her own billing party, which is why household membership
        // is optional rather than a family being invented for her.
        var ruth = new Member("Ruth", "Feldman", new DateOnly(1978, 2, 3),
            email: "ruth.f@example.com") { StudioId = studioId };
        ruth.GrantRole(StudioRole.Student, new DateOnly(2025, 1, 6));

        db.Members.AddRange(mei, luis, priya, nadia, amara, chidi, sofia, diego, elena, ruth);

        // --- Classes --------------------------------------------------------------------

        var preBallet = new DanceClass(fall, "Pre-Ballet", "Ballet", capacity: 10, room: studioC,
            level: "Introductory", minimumAge: 3, maximumAge: 4);
        preBallet.AddMeetingPattern(DayOfWeek.Saturday, new TimeOnly(9, 0), TimeSpan.FromMinutes(45));
        preBallet.Assign(priya, AssignmentRole.Lead);

        var beginnerBallet = new DanceClass(fall, "Beginner Ballet", "Ballet", capacity: 12,
            room: studioB, level: "Level 1", minimumAge: 5, maximumAge: 7);
        beginnerBallet.AddMeetingPattern(DayOfWeek.Tuesday, new TimeOnly(16, 0), TimeSpan.FromHours(1));
        beginnerBallet.Assign(mei, AssignmentRole.Lead);

        // A sixteen-year-old assisting five-year-olds. She is far outside the class's own age
        // range, which is precisely why teaching and attending cannot share an entity.
        beginnerBallet.Assign(nadia, AssignmentRole.Assistant);

        // Meets twice a week — the reason meeting patterns are a collection and not two columns.
        var intermediateBallet = new DanceClass(fall, "Intermediate Ballet", "Ballet", capacity: 14,
            room: studioA, level: "Level 3", minimumAge: 11, maximumAge: 15);
        intermediateBallet.AddMeetingPattern(DayOfWeek.Tuesday, new TimeOnly(17, 0), TimeSpan.FromMinutes(90));
        intermediateBallet.AddMeetingPattern(DayOfWeek.Thursday, new TimeOnly(17, 0), TimeSpan.FromMinutes(90));
        intermediateBallet.Assign(mei, AssignmentRole.Lead);

        var teenHipHop = new DanceClass(fall, "Teen Hip-Hop", "Hip-Hop", capacity: 20,
            room: studioA, minimumAge: 12, maximumAge: 17);
        teenHipHop.AddMeetingPattern(DayOfWeek.Wednesday, new TimeOnly(18, 0), TimeSpan.FromHours(1));
        teenHipHop.Assign(luis, AssignmentRole.Lead);

        // No upper age bound, and no level: an open adult class.
        var adultTap = new DanceClass(fall, "Adult Tap", "Tap", capacity: 16, room: studioC,
            minimumAge: 18);
        adultTap.AddMeetingPattern(DayOfWeek.Thursday, new TimeOnly(19, 0), TimeSpan.FromHours(1));
        adultTap.Assign(priya, AssignmentRole.Lead);

        // A substitute for a single week — one of the four things a single InstructorId foreign
        // key cannot express, alongside the co-teaching, assistant, and mid-term-change cases
        // elsewhere in this seed.
        adultTap.Assign(luis, AssignmentRole.Substitute,
            from: new DateOnly(2026, 11, 12), until: new DateOnly(2026, 11, 12));

        // A class with no meeting pattern at all. Not a broken record — a rehearsal series whose
        // dates are set as the performance approaches, and the shape that falls out for free
        // once patterns are treated as generators rather than as the schedule itself.
        var nutcracker = new DanceClass(fall, "Nutcracker Rehearsals", "Ballet", capacity: 30,
            room: studioA, startDate: new DateOnly(2026, 10, 26), endDate: new DateOnly(2026, 12, 19));
        nutcracker.Assign(mei, AssignmentRole.Lead);
        nutcracker.Assign(luis, AssignmentRole.Lead); // co-taught

        db.DanceClasses.AddRange(
            preBallet, beginnerBallet, intermediateBallet, teenHipHop, adultTap, nutcracker);

        await db.SaveChangesAsync(cancellationToken);
    }
}
