using Microsoft.EntityFrameworkCore;
using Pirouette.Infrastructure;

namespace PirouetteApp.Services;

/// <summary>
/// Reads for the class schedule. See <see cref="MemberService"/> for why this takes a context
/// factory and why nothing here returns an entity.
/// </summary>
public sealed class ClassService(IDbContextFactory<PirouetteDbContext> contextFactory)
{
    public async Task<IReadOnlyList<ClassListItem>> ListAsync(
        Guid? termId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var classes = db.DanceClasses.AsNoTracking();

        if (termId is { } id)
        {
            classes = classes.Where(c => c.TermId == id);
        }

        return await Project(classes).ToListAsync(cancellationToken);
    }

    public async Task<ClassListItem?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await Project(db.DanceClasses.AsNoTracking().Where(c => c.Id == id))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TermOption>> ListTermsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Terms.AsNoTracking()
            .OrderByDescending(t => t.StartDate)
            .Select(t => new TermOption(t.Id, t.Name, t.StartDate, t.EndDate))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The one projection both reads share, so a column added to the class page cannot be
    /// forgotten on the list page.
    /// </summary>
    /// <remarks>
    /// The nested collections are ordered here rather than in the markup, except for
    /// assignment role: that is stored as text so the database would sort it alphabetically —
    /// Assistant, Lead, Substitute — which puts the assistant above the lead they assist. It is
    /// ordered by enum value after materialisation instead, where Lead comes first because the
    /// enum was declared in that order.
    /// </remarks>
    private static IQueryable<ClassListItem> Project(IQueryable<Pirouette.Domain.DanceClass> classes) =>
        from c in classes
        orderby c.Name
        select new ClassListItem(
            c.Id,
            c.Name,
            c.Style,
            c.Level,
            // Navigation rather than a join, unlike Member and Household: a class genuinely has
            // a room, so the navigation is part of the model and not something added for a view.
            c.Room == null ? null : c.Room.Name,
            c.Capacity,
            c.MinimumAge,
            c.MaximumAge,
            c.StartDate,
            c.EndDate,
            c.MeetingPatterns
                .OrderBy(p => p.DayOfWeek)
                .ThenBy(p => p.StartTime)
                .Select(p => new MeetingPatternView(p.DayOfWeek, p.StartTime, p.Duration))
                .ToList(),
            c.Assignments
                .Select(a => new ClassAssignmentView(
                    a.MemberId,
                    a.Member!.FirstName + " " + a.Member.LastName,
                    a.Role,
                    a.From,
                    a.Until))
                .ToList());
}

public sealed record TermOption(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate)
{
    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;
}
