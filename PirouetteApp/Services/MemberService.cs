using Microsoft.EntityFrameworkCore;
using Pirouette.Domain;
using Pirouette.Infrastructure;

namespace PirouetteApp.Services;

/// <summary>
/// Reads and writes for the member directory.
/// </summary>
/// <remarks>
/// Takes an <see cref="IDbContextFactory{TContext}"/> rather than a context. Blazor Server
/// components are long-lived and their event handlers can overlap, so a single injected
/// <see cref="DbContext"/> — which is not thread-safe and forbids concurrent operations —
/// eventually throws under an impatient double-click. A short-lived context per operation is
/// the supported pattern, and it keeps each unit of work's change tracker empty.
///
/// <para>Every read is <c>AsNoTracking</c> and projects to a record. Nothing here returns an
/// entity: see <see cref="MemberListItem"/> for why that boundary is drawn deliberately rather
/// than for tidiness.</para>
/// </remarks>
public sealed class MemberService(IDbContextFactory<PirouetteDbContext> contextFactory)
{
    public async Task<IReadOnlyList<MemberListItem>> ListAsync(
        string? search, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var members = db.Members.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";

            // ILike rather than ToLower().Contains(): it maps to Postgres' native
            // case-insensitive operator instead of forcing a function call over every row,
            // which would make an index on the name columns unusable.
            members = members.Where(m =>
                EF.Functions.ILike(m.FirstName, pattern) ||
                EF.Functions.ILike(m.LastName, pattern));
        }

        // The cast on the join key is load-bearing, not noise: HouseholdId is a nullable Guid
        // and Household.Id is not, and without it the two key types differ and the join fails
        // to compile with an inference error that says nothing about the real cause.
        //
        // Left join rather than a navigation property: Member holds a HouseholdId but has no
        // Household navigation, because a member's identity does not depend on their billing
        // arrangement and adding the navigation purely so a list page can show a name would
        // put that coupling in the domain to serve the UI.
        var query =
            from m in members
            join h in db.Households.AsNoTracking() on m.HouseholdId equals (Guid?)h.Id into households
            from household in households.DefaultIfEmpty()
            orderby m.LastName, m.FirstName
            select new MemberListItem(
                m.Id,
                m.FirstName,
                m.LastName,
                m.DateOfBirth,
                m.Email,
                household == null ? null : household.Name,
                m.Roles
                    .Where(r => r.From <= asOf && (r.Until == null || r.Until >= asOf))
                    .Select(r => r.Role)
                    .ToList());

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<MemberDetailView?> GetAsync(
        Guid id, DateOnly asOf, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var query =
            from m in db.Members.AsNoTracking()
            where m.Id == id
            join h in db.Households.AsNoTracking() on m.HouseholdId equals (Guid?)h.Id into households
            from household in households.DefaultIfEmpty()
            select new MemberDetailView(
                m.Id,
                m.FirstName,
                m.LastName,
                m.DateOfBirth,
                m.Email,
                m.Phone,
                m.HouseholdId,
                household == null ? null : household.Name,
                m.Roles
                    .OrderBy(r => r.From)
                    .Select(r => new MemberRoleView(r.Role, r.From, r.Until))
                    .ToList(),
                // Reached through DanceClass rather than through a DbSet on ClassAssignment.
                // Assignments belong to the class aggregate; asking the class which of its
                // assignments name this member keeps that direction intact and needs no extra
                // DbSet whose only purpose would be to bypass it.
                (from c in db.DanceClasses.AsNoTracking()
                 from a in c.Assignments
                 where a.MemberId == id
                 orderby c.Name
                 select new MemberTeachingView(c.Id, c.Name, a.Role, a.From, a.Until))
                .ToList());

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HouseholdOption>> ListHouseholdsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await db.Households.AsNoTracking()
            .OrderBy(h => h.Name)
            .Select(h => new HouseholdOption(h.Id, h.Name))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Creates a member with an optional initial role.
    /// </summary>
    /// <remarks>
    /// The domain constructor throws on invalid input rather than returning a result, and that
    /// exception is allowed to propagate to the page, which catches it and shows the message.
    /// Translating it into a result type here would mean inventing an error vocabulary that
    /// duplicates what the guard clauses already say.
    /// </remarks>
    public async Task<Guid> CreateAsync(
        string firstName,
        string lastName,
        DateOnly? dateOfBirth,
        string? email,
        string? phone,
        Guid? householdId,
        StudioRole? initialRole,
        DateOnly roleFrom,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Loaded rather than trusted, so a household id from a stale form for a studio that no
        // longer owns it fails here instead of becoming a foreign key violation. The query
        // filter is what makes this check meaningful: another studio's household is not
        // findable, so it comes back null.
        Household? household = null;

        if (householdId is { } id)
        {
            household = await db.Households.FirstOrDefaultAsync(h => h.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("That household no longer exists.");
        }

        var member = new Member(firstName, lastName, dateOfBirth, email, phone, household);

        if (initialRole is { } role)
        {
            member.GrantRole(role, roleFrom);
        }

        db.Members.Add(member);

        // StudioId is left unset on purpose: the SaveChanges interceptor stamps it. Setting it
        // here would work, but it would also be one more place that has to remember to.
        await db.SaveChangesAsync(cancellationToken);

        return member.Id;
    }

    public async Task UpdateAsync(
        Guid id,
        string firstName,
        string lastName,
        DateOnly? dateOfBirth,
        string? email,
        string? phone,
        Guid? householdId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Tracked, unlike every read above: this one is going to be written back.
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("That member no longer exists.");

        member.UpdateDetails(firstName, lastName, dateOfBirth, email, phone);

        Household? household = null;

        if (householdId is { } hid)
        {
            household = await db.Households.FirstOrDefaultAsync(h => h.Id == hid, cancellationToken)
                ?? throw new InvalidOperationException("That household no longer exists.");
        }

        member.AssignToHousehold(household);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Grants a role to an existing member.</summary>
    public async Task GrantRoleAsync(
        Guid memberId, StudioRole role, DateOnly from, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Include, not a bare lookup: GrantRole rejects a grant overlapping one already held,
        // and it can only do that if the roles it already holds are loaded. Without the
        // Include the collection is empty, every grant looks like the first, and the check
        // silently passes — a lazy-loading habit turning into a data bug.
        var member = await db.Members
            .Include(m => m.Roles)
            .FirstOrDefaultAsync(m => m.Id == memberId, cancellationToken)
            ?? throw new InvalidOperationException("That member no longer exists.");

        member.GrantRole(role, from);

        await db.SaveChangesAsync(cancellationToken);
    }
}
