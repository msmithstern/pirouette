namespace Pirouette.Domain;

/// <summary>
/// A role held by a <see cref="Member"/> over a period of time. A member may hold several at
/// once.
/// </summary>
/// <remarks>
/// This replaces what would otherwise be boolean flags on <see cref="Member"/>
/// (<c>IsStudent</c>, <c>IsInstructor</c>). Flags fail four ways: they cannot answer "was she
/// teaching in Fall 2025?", adding a role becomes a schema migration, N booleans admit 2^N
/// states of which most are nonsense, and there is nowhere to record when the role began.
///
/// <para>Created through <see cref="Member.GrantRole"/> rather than directly, so the member
/// aggregate can reject overlapping grants of the same role before one exists.</para>
/// </remarks>
public sealed class MemberRole : ITenantOwned
{
    /// <summary>Constructor used only by EF Core. See <see cref="Studio"/> for why.</summary>
    private MemberRole() { }

    internal MemberRole(Guid memberId, StudioRole role, DateOnly from, DateOnly? until)
    {
        if (until is { } end && end < from)
        {
            throw new ArgumentOutOfRangeException(
                nameof(until),
                $"Role end date ({end}) cannot be before its start date ({from}).");
        }

        Id = Guid.CreateVersion7();
        MemberId = memberId;
        Role = role;
        From = from;
        Until = until;
    }

    public Guid Id { get; private set; }

    public Guid StudioId { get; set; }

    public Guid MemberId { get; private set; }

    public StudioRole Role { get; private set; }

    /// <summary>First day the role is held, inclusive.</summary>
    public DateOnly From { get; private set; }

    /// <summary>
    /// Last day the role is held, inclusive. Null means open-ended — the usual case for a
    /// current student or a currently employed instructor.
    /// </summary>
    public DateOnly? Until { get; private set; }

    /// <summary>Whether the role is held on <paramref name="date"/>, inclusive of both bounds.</summary>
    public bool IsActiveOn(DateOnly date) => date >= From && (Until is null || date <= Until);

    /// <summary>
    /// Whether this role's period overlaps <paramref name="other"/>'s at any point.
    /// </summary>
    /// <remarks>
    /// Open-ended periods (<see cref="Until"/> of null) are treated as extending forever, so
    /// two open-ended grants always overlap. Written as "neither ends before the other starts"
    /// rather than by enumerating the cases, because the four-way version is where interval
    /// comparison bugs live.
    /// </remarks>
    internal bool OverlapsWith(MemberRole other) =>
        (Until is null || Until >= other.From) &&
        (other.Until is null || other.Until >= From);

    /// <summary>
    /// Closes an open-ended role, or shortens a dated one. Used when a student graduates or an
    /// instructor leaves.
    /// </summary>
    /// <remarks>
    /// Ending a role is not the same as deleting the row. "She stopped teaching in June" and
    /// "she never taught" are different facts, and only the first can answer a question about
    /// last autumn's rosters.
    /// </remarks>
    public void EndOn(DateOnly date)
    {
        if (date < From)
        {
            throw new ArgumentOutOfRangeException(
                nameof(date),
                $"Cannot end role {Role} on {date}: it did not begin until {From}.");
        }

        Until = date;
    }
}
