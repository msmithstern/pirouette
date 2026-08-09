using Pirouette.Domain;

namespace PirouetteApp.Services;

/// <summary>
/// Shapes returned to the UI. Deliberately not the domain entities.
/// </summary>
/// <remarks>
/// Two reasons, and the second is the one that matters. The cheap reason is performance:
/// projecting with <c>Select</c> lets EF fetch only the columns a page renders instead of
/// loading whole entity graphs, and it composes with <c>AsNoTracking</c> so nothing is put in
/// the change tracker that will never be saved.
///
/// <para>The real reason is that field-level privacy is coming. A sixteen-year-old assistant
/// needs the roster for the class she teaches and must not see her classmates' home addresses
/// or medical notes — a rule that row-level scoping cannot express, because the row is
/// permitted and only some of its columns are not. The enforcement point for that is returning
/// audience-specific projections rather than entities, so the projection layer has to exist
/// before there is anything sensitive to leak through it. Adding it later would mean auditing
/// every page that had grown used to holding an entity.</para>
/// </remarks>
public sealed record MemberListItem(
    Guid Id,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    string? Email,
    string? HouseholdName,
    IReadOnlyList<StudioRole> CurrentRoles)
{
    public string FullName => $"{FirstName} {LastName}";

    /// <summary>
    /// Computed after materialisation, from a date of birth read as a plain column.
    /// </summary>
    /// <remarks>
    /// The alternative — expressing the completed-years calculation inside the LINQ projection
    /// so Postgres does the arithmetic — reads worse, depends on Npgsql translating
    /// <c>DateOnly.AddYears</c>, and would be a second copy of a definition that already
    /// exists in <see cref="Pirouette.Domain.Age"/>. Ages are computed over a page of rows,
    /// not filtered on, so there is nothing to gain by pushing it down.
    /// </remarks>
    public int? AgeOn(DateOnly asOf) => Age.InCompletedYears(DateOfBirth, asOf);
}

public sealed record MemberRoleView(StudioRole Role, DateOnly From, DateOnly? Until)
{
    public bool IsCurrent(DateOnly asOf) => asOf >= From && (Until is null || asOf <= Until);
}

public sealed record MemberTeachingView(
    Guid DanceClassId,
    string ClassName,
    AssignmentRole Role,
    DateOnly From,
    DateOnly? Until);

public sealed record MemberDetailView(
    Guid Id,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth,
    string? Email,
    string? Phone,
    Guid? HouseholdId,
    string? HouseholdName,
    IReadOnlyList<MemberRoleView> Roles,
    IReadOnlyList<MemberTeachingView> Teaching)
{
    public string FullName => $"{FirstName} {LastName}";

    public int? AgeOn(DateOnly asOf) => Age.InCompletedYears(DateOfBirth, asOf);

    public bool IsStaffOn(DateOnly asOf) =>
        Roles.Any(r => r.IsCurrent(asOf) && r.Role is
            StudioRole.Owner or StudioRole.Admin or StudioRole.Staff or StudioRole.AssistantStaff);
}

/// <summary>
/// Carries the duration rather than an end time, and computes the end after materialisation.
/// </summary>
/// <remarks>
/// Mirrors the domain, but also avoids asking the database to add an interval to a time — a
/// translation Npgsql may or may not support depending on the shape of the expression, and
/// one that would wrap at midnight if it did. Three plain column reads and a computation in
/// C# is both cheaper and less surprising.
/// </remarks>
public sealed record MeetingPatternView(DayOfWeek DayOfWeek, TimeOnly StartTime, TimeSpan Duration)
{
    public TimeOnly EndTime => StartTime.Add(Duration);

    public override string ToString() => $"{DayOfWeek}s {StartTime:HH:mm}–{EndTime:HH:mm}";
}

public sealed record ClassAssignmentView(
    Guid MemberId,
    string MemberName,
    AssignmentRole Role,
    DateOnly From,
    DateOnly? Until)
{
    public bool IsCurrent(DateOnly asOf) => asOf >= From && (Until is null || asOf <= Until);
}

public sealed record ClassListItem(
    Guid Id,
    string Name,
    string Style,
    string? Level,
    string? RoomName,
    int Capacity,
    int? MinimumAge,
    int? MaximumAge,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<MeetingPatternView> MeetingPatterns,
    IReadOnlyList<ClassAssignmentView> Assignments)
{
    /// <summary>
    /// "Ages 5–7", "Ages 18+", "All ages". Formatted here rather than in the markup so the
    /// three cases are handled once instead of in every page that shows a class.
    /// </summary>
    public string AgeRange => (MinimumAge, MaximumAge) switch
    {
        (null, null) => "All ages",
        ({ } min, null) => $"Ages {min}+",
        (null, { } max) => $"Up to {max}",
        ({ } min, { } max) when min == max => $"Age {min}",
        ({ } min, { } max) => $"Ages {min}–{max}"
    };

    /// <summary>
    /// Empty when the class has no recurring pattern — a workshop or rehearsal series whose
    /// dates are entered individually. Not an error state, so it reads as a plain statement.
    /// </summary>
    public string ScheduleSummary => MeetingPatterns.Count == 0
        ? "No recurring schedule"
        : string.Join(" · ", MeetingPatterns.Select(p => p.ToString()));
}

public sealed record HouseholdOption(Guid Id, string Name);
