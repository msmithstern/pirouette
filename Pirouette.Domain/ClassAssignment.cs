namespace Pirouette.Domain;

/// <summary>
/// A member teaching a class, in a stated capacity, over a stated period.
/// </summary>
/// <remarks>
/// This replaces what would naturally be written as <c>DanceClass.InstructorId</c>. A single
/// foreign key cannot express any of the four things a studio does routinely: co-teaching, an
/// assistant working alongside a lead, a substitute covering one week, or an instructor
/// changing mid-term. Each of those would need its own workaround; a row per member-per-class
/// with a role and effective dates handles all four with no special cases.
///
/// <para>Created through <see cref="DanceClass.Assign"/> so that the "assignment dates fall
/// within the class's dates" invariant has somewhere to be checked — the assignment alone
/// cannot see the class it belongs to.</para>
/// </remarks>
public sealed class ClassAssignment : ITenantOwned
{
    /// <summary>Constructor used only by EF Core. See <see cref="Studio"/> for why.</summary>
    private ClassAssignment() { }

    internal ClassAssignment(Guid danceClassId, Guid memberId, AssignmentRole role,
        DateOnly from, DateOnly? until)
    {
        if (until is { } end && end < from)
        {
            throw new ArgumentOutOfRangeException(
                nameof(until),
                $"Assignment end date ({end}) cannot be before its start date ({from}).");
        }

        Id = Guid.CreateVersion7();
        DanceClassId = danceClassId;
        MemberId = memberId;
        Role = role;
        From = from;
        Until = until;
    }

    public Guid Id { get; private set; }

    public Guid StudioId { get; set; }

    public Guid DanceClassId { get; private set; }

    public Guid MemberId { get; private set; }

    public AssignmentRole Role { get; private set; }

    public DateOnly From { get; private set; }

    /// <summary>
    /// Last day of the assignment, inclusive. Null means "for the rest of the class", which is
    /// the normal case for a lead instructor.
    /// </summary>
    public DateOnly? Until { get; private set; }

    /// <summary>Convenience navigation, populated when the caller asks for it.</summary>
    public Member? Member { get; private set; }

    public bool IsActiveOn(DateOnly date) => date >= From && (Until is null || date <= Until);

    /// <summary>
    /// Whether this assignment's period overlaps <paramref name="other"/>'s at any point.
    /// See <see cref="MemberRole.OverlapsWith"/> for why it is written this way round.
    /// </summary>
    internal bool OverlapsWith(ClassAssignment other) =>
        (Until is null || Until >= other.From) &&
        (other.Until is null || other.Until >= From);

    /// <summary>Ends the assignment. See <see cref="MemberRole.EndOn"/> on why this is not a delete.</summary>
    public void EndOn(DateOnly date)
    {
        if (date < From)
        {
            throw new ArgumentOutOfRangeException(
                nameof(date),
                $"Cannot end an assignment on {date}: it did not begin until {From}.");
        }

        Until = date;
    }
}
