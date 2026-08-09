using Microsoft.EntityFrameworkCore;
using Pirouette.Domain;
using Pirouette.Infrastructure;

namespace PirouetteApp.Services;

public sealed record StudioOverview(
    int ActiveStudents,
    int ActiveStaff,
    int Households,
    int Rooms,
    int ClassesInTerm,
    TermOption? CurrentTerm);

/// <summary>
/// The counts on the dashboard.
/// </summary>
/// <remarks>
/// Separate from <see cref="MemberService"/> and <see cref="ClassService"/> because it is a
/// different kind of question: those answer "show me these rows", this answers "how many".
/// Loading the lists and counting them in memory would give the same numbers today and would
/// keep doing so right up until a studio has a few thousand members, at which point the
/// dashboard would be transferring the entire directory to render six digits.
/// </remarks>
public sealed class DashboardService(IDbContextFactory<PirouetteDbContext> contextFactory)
{
    public async Task<StudioOverview> GetAsync(
        DateOnly asOf, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The term containing today, falling back to the most recent one so the dashboard is
        // never blank between terms — an empty screen in August would look like a bug rather
        // than like the summer holidays.
        var currentTerm = await db.Terms.AsNoTracking()
            .Where(t => t.StartDate <= asOf && t.EndDate >= asOf)
            .Select(t => new TermOption(t.Id, t.Name, t.StartDate, t.EndDate))
            .FirstOrDefaultAsync(cancellationToken)
            ?? await db.Terms.AsNoTracking()
                .OrderByDescending(t => t.StartDate)
                .Select(t => new TermOption(t.Id, t.Name, t.StartDate, t.EndDate))
                .FirstOrDefaultAsync(cancellationToken);

        // Counted over members rather than over roles: someone holding both Owner and Staff is
        // one member of staff, and counting role rows would report them twice.
        var activeStudents = await db.Members.AsNoTracking()
            .CountAsync(
                m => m.Roles.Any(r =>
                    r.Role == StudioRole.Student &&
                    r.From <= asOf &&
                    (r.Until == null || r.Until >= asOf)),
                cancellationToken);

        var activeStaff = await db.Members.AsNoTracking()
            .CountAsync(
                // Spelled out rather than written as a `Contains` over an array of roles.
                // The enum is stored through a value converter, and while EF can usually
                // translate a containment check over a converted column, four ORs are certain
                // to translate and cost nothing to read.
                m => m.Roles.Any(r =>
                    (r.Role == StudioRole.Owner ||
                     r.Role == StudioRole.Admin ||
                     r.Role == StudioRole.Staff ||
                     r.Role == StudioRole.AssistantStaff) &&
                    r.From <= asOf &&
                    (r.Until == null || r.Until >= asOf)),
                cancellationToken);

        var households = await db.Households.AsNoTracking().CountAsync(cancellationToken);
        var rooms = await db.Rooms.AsNoTracking().CountAsync(cancellationToken);

        var classesInTerm = currentTerm is null
            ? 0
            : await db.DanceClasses.AsNoTracking()
                .CountAsync(c => c.TermId == currentTerm.Id, cancellationToken);

        return new StudioOverview(
            activeStudents, activeStaff, households, rooms, classesInTerm, currentTerm);
    }
}
