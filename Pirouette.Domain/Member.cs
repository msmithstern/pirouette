namespace Pirouette.Domain;

/// <summary>
/// One row per human involved with the studio, whatever their relationship to it.
/// </summary>
/// <remarks>
/// Not <c>Student</c> and <c>Instructor</c> as separate entities. That model breaks the moment
/// a teenager assists with the beginner class: two rows for one person, two birthdays to keep
/// in sync, and no way to answer "is this the same human?" without matching on name. The adult
/// student who also teaches, the former student hired as staff, and the parent taking an adult
/// tap class all break it too — the student-teacher just breaks it first and most visibly.
///
/// <para>Not <c>User</c> either. Most people here never sign in; a six-year-old ballet student
/// has no account and never will, so "someone who authenticates" would be untrue of the
/// majority of rows. It also collides badly in ASP.NET Core, where <c>ApplicationUser</c>,
/// <c>HttpContext.User</c>, and <c>ClaimsPrincipal</c> are all ambient. Authentication gets its
/// own entity mapped 1:1 onto this one when it arrives.</para>
///
/// <para>This is the aggregate root for roles: <see cref="MemberRole"/> instances are created
/// only through <see cref="GrantRole"/>, so the no-overlapping-duplicates rule has a single
/// enforcement point rather than being the caller's responsibility.</para>
/// </remarks>
public sealed class Member : ITenantOwned
{
    private readonly List<MemberRole> _roles = [];

    /// <summary>Constructor used only by EF Core. See <see cref="Studio"/> for why.</summary>
    private Member() { }

    public Member(
        string firstName,
        string lastName,
        DateOnly? dateOfBirth = null,
        string? email = null,
        string? phone = null,
        Household? household = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

        // Compared against UTC today rather than local. The studio's own timezone would be
        // more correct, but requiring a Studio here to validate a birthday is a heavy
        // dependency for a check whose only job is catching a typo'd year. A date of birth
        // that is wrong by less than a day is not the failure mode this guards against.
        if (dateOfBirth is { } dob && dob > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dateOfBirth),
                $"Date of birth ({dob}) is in the future.");
        }

        Id = Guid.CreateVersion7();
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        DateOfBirth = dateOfBirth;
        Email = Normalise(email);
        Phone = Normalise(phone);
        HouseholdId = household?.Id;
    }

    public Guid Id { get; private set; }

    public Guid StudioId { get; set; }

    public string FirstName { get; private set; } = null!;

    public string LastName { get; private set; } = null!;

    public string FullName => $"{FirstName} {LastName}";

    /// <summary>
    /// Optional. Age gates class eligibility and decides whether a guardian is required, so it
    /// matters for students — but an adult in a drop-in tap class has no reason to give it and
    /// a staff member's birthday is not the studio's business. Null means "not recorded",
    /// which is a real and common state rather than missing data to be chased.
    /// </summary>
    public DateOnly? DateOfBirth { get; private set; }

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>
    /// The billing unit this member belongs to, if any. Null for members who are their own
    /// billing party.
    /// </summary>
    public Guid? HouseholdId { get; private set; }

    /// <summary>
    /// Roles held, past and present. Exposed read-only: mutating this list directly would
    /// bypass the overlap check in <see cref="GrantRole"/>.
    /// </summary>
    public IReadOnlyCollection<MemberRole> Roles => _roles.AsReadOnly();

    /// <summary>
    /// Age in completed years on <paramref name="asOf"/>, or null if no date of birth is recorded.
    /// </summary>
    /// <remarks>
    /// Computed rather than stored, because a stored age is wrong within a year of being
    /// written and there is no event to recompute it on. The arithmetic lives in
    /// <see cref="Age"/> so the read models the UI is handed can use the same definition.
    /// </remarks>
    public int? AgeOn(DateOnly asOf) => Age.InCompletedYears(DateOfBirth, asOf);

    /// <summary>Whether the member holds <paramref name="role"/> on <paramref name="asOf"/>.</summary>
    public bool HasRole(StudioRole role, DateOnly asOf) =>
        _roles.Any(r => r.Role == role && r.IsActiveOn(asOf));

    /// <summary>
    /// Whether the member works for the studio in any capacity on <paramref name="asOf"/>.
    /// </summary>
    /// <remarks>
    /// A computed method rather than a stored column, so it reads with the ergonomics of a
    /// boolean flag while staying time-aware and needing no migration when the set of staff
    /// roles changes. Note that it takes a date and has no parameterless overload: "is she
    /// staff?" is not answerable without saying when, and offering a convenient
    /// <c>IsStaff</c> property would quietly reintroduce the flag this model exists to avoid.
    /// </remarks>
    public bool IsStaffOn(DateOnly asOf) =>
        HasRole(StudioRole.Owner, asOf) ||
        HasRole(StudioRole.Admin, asOf) ||
        HasRole(StudioRole.Staff, asOf) ||
        HasRole(StudioRole.AssistantStaff, asOf);

    /// <summary>
    /// Grants a role for a period, and returns it.
    /// </summary>
    /// <remarks>
    /// Re-granting a role the member already held is legitimate — a student who leaves and
    /// returns, an instructor rehired for a second season — so this rejects only grants whose
    /// periods <em>overlap</em>. Two simultaneous, identical roles are not a fact about the
    /// world; they are a double-submitted form, and they make "when did she become staff?"
    /// ambiguous.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The member already holds this role over an overlapping period.
    /// </exception>
    public MemberRole GrantRole(StudioRole role, DateOnly from, DateOnly? until = null)
    {
        var granted = new MemberRole(Id, role, from, until) { StudioId = StudioId };

        var conflicting = _roles.FirstOrDefault(r => r.Role == role && r.OverlapsWith(granted));

        if (conflicting is not null)
        {
            throw new InvalidOperationException(
                $"{FullName} already holds the {role} role from {conflicting.From} " +
                $"to {conflicting.Until?.ToString() ?? "open-ended"}, which overlaps the " +
                $"requested period starting {from}.");
        }

        _roles.Add(granted);

        return granted;
    }

    public void UpdateDetails(string firstName, string lastName, DateOnly? dateOfBirth,
        string? email, string? phone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

        if (dateOfBirth is { } dob && dob > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dateOfBirth),
                $"Date of birth ({dob}) is in the future.");
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        DateOfBirth = dateOfBirth;
        Email = Normalise(email);
        Phone = Normalise(phone);
    }

    /// <summary>Moves the member into a household, or out of one when passed null.</summary>
    public void AssignToHousehold(Household? household) => HouseholdId = household?.Id;

    /// <summary>See <see cref="Household"/> for why blank collapses to null.</summary>
    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
