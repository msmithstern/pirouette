namespace Pirouette.Domain;

/// <summary>
/// A recurring class definition — "Intermediate Ballet, Tuesdays 16:00–17:00, Studio A, Fall
/// term". The thing a studio publishes and enrols students into.
/// </summary>
/// <remarks>
/// Named <c>DanceClass</c> because <c>class</c> is a C# keyword and <c>@class</c> everywhere
/// would be worse than a slightly long name.
///
/// <para><b>Definition, not occurrence.</b> This is one half of a deliberate split. The other
/// half is <c>ClassSession</c>: a concrete dated row generated from the meeting patterns here,
/// which is where reality gets attached — attendance, a snow-day cancellation, a substitute
/// for one date, a room change while the floor is refinished. Computing occurrences on the fly
/// works right up until the first deviation from the rule, which for a dance studio is roughly
/// week three, and then there is nowhere to record it.</para>
///
/// <para>This type is the aggregate root for its meeting patterns and teaching assignments.
/// Both are created through methods here rather than constructed by callers, because both have
/// invariants that can only be checked against the class: a pattern must not overlap a sibling
/// pattern, and an assignment must fall within the class's own dates. Neither child can see
/// enough to check itself.</para>
/// </remarks>
public sealed class DanceClass : ITenantOwned
{
    private readonly List<MeetingPattern> _meetingPatterns = [];
    private readonly List<ClassAssignment> _assignments = [];

    /// <summary>Constructor used only by EF Core. See <see cref="Studio"/> for why.</summary>
    private DanceClass() { }

    /// <param name="term">
    /// The term this class runs in. Taken as an entity rather than an id so the constructor can
    /// check the class's dates against the term's — a check that is impossible with only a
    /// <see cref="Guid"/> in hand, and that would otherwise have to live in a service where it
    /// is easy to forget to call.
    /// </param>
    /// <param name="room">
    /// Where the class usually meets. Optional: a class may be scheduled before a room is
    /// decided, and individual sessions can override it anyway.
    /// </param>
    /// <param name="startDate">Defaults to the term's start. Must fall within the term.</param>
    /// <param name="endDate">Defaults to the term's end. Must fall within the term.</param>
    public DanceClass(
        Term term,
        string name,
        string style,
        int capacity,
        Room? room = null,
        string? level = null,
        int? minimumAge = null,
        int? maximumAge = null,
        DateOnly? startDate = null,
        DateOnly? endDate = null)
    {
        ArgumentNullException.ThrowIfNull(term);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        if (minimumAge is { } min)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(min, nameof(minimumAge));
        }

        if (maximumAge is { } max)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(max, nameof(maximumAge));
        }

        if (minimumAge is { } lower && maximumAge is { } upper && lower > upper)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAge),
                $"Minimum age ({lower}) cannot exceed maximum age ({upper}).");
        }

        var start = startDate ?? term.StartDate;
        var end = endDate ?? term.EndDate;

        if (end < start)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endDate),
                $"Class end date ({end}) cannot be before its start date ({start}).");
        }

        if (!term.Contains(start) || !term.Contains(end))
        {
            throw new ArgumentOutOfRangeException(
                nameof(startDate),
                $"Class dates ({start} to {end}) must fall within term \"{term.Name}\" " +
                $"({term.StartDate} to {term.EndDate}).");
        }

        Id = Guid.CreateVersion7();

        // Inherited from the term rather than left for the insert interceptor to stamp. The
        // interceptor only fills in an unset tenant, so this is not a conflict — but taking it
        // from the parent means a class constructed in memory already knows which studio it
        // belongs to, which is what makes the guard below able to compare anything at all.
        // It has to be assigned before the guard runs, or the comparison is against Guid.Empty
        // and passes vacuously.
        StudioId = term.StudioId;

        GuardSameStudio(room);

        TermId = term.Id;
        RoomId = room?.Id;
        Name = name.Trim();
        Style = style.Trim();
        Level = string.IsNullOrWhiteSpace(level) ? null : level.Trim();
        Capacity = capacity;
        MinimumAge = minimumAge;
        MaximumAge = maximumAge;
        StartDate = start;
        EndDate = end;
    }

    public Guid Id { get; private set; }

    public Guid StudioId { get; set; }

    public Guid TermId { get; private set; }

    /// <summary>Where the class usually meets. Null until a room is assigned.</summary>
    public Guid? RoomId { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>Ballet, Tap, Jazz, Hip-Hop, Contemporary. A free-text label, not an enum.</summary>
    /// <remarks>
    /// Studios invent styles and rename them, and an enum would turn "we're offering Acro next
    /// term" into a schema migration and a deployment. If this ever needs to be constrained it
    /// wants to be a tenant-owned lookup table the studio can edit, not a compile-time list.
    /// </remarks>
    public string Style { get; private set; } = null!;

    /// <summary>"Beginner", "Level 3", "Pre-Pointe". Optional — an open adult class has none.</summary>
    public string? Level { get; private set; }

    /// <summary>Maximum enrolment. Distinct from the room's capacity, and usually lower.</summary>
    public int Capacity { get; private set; }

    /// <summary>Inclusive lower bound on a dancer's age, in years. Null means no lower bound.</summary>
    public int? MinimumAge { get; private set; }

    /// <summary>Inclusive upper bound. Null means no upper bound — the usual case for adult classes.</summary>
    public int? MaximumAge { get; private set; }

    /// <summary>
    /// First and last day the class runs. Usually the whole term, but a six-week workshop
    /// inside a sixteen-week term is a normal thing to want.
    /// </summary>
    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    /// <summary>
    /// The weekly slots this class occupies. Empty is valid and means "no recurring schedule" —
    /// a workshop series whose dates are all entered individually.
    /// </summary>
    public IReadOnlyCollection<MeetingPattern> MeetingPatterns => _meetingPatterns.AsReadOnly();

    public IReadOnlyCollection<ClassAssignment> Assignments => _assignments.AsReadOnly();

    /// <summary>Convenience navigations, populated when the caller asks for them.</summary>
    public Term? Term { get; private set; }

    public Room? Room { get; private set; }

    public bool RunsOn(DateOnly date) => date >= StartDate && date <= EndDate;

    /// <summary>Whether a dancer of <paramref name="age"/> years falls inside the age range.</summary>
    /// <remarks>
    /// An unknown age (null) passes. The alternative — refusing to enrol anyone whose birthday
    /// is not on file — turns a missing optional field into a blocking error, and studios would
    /// respond by typing in a fake date, which is worse than knowing the age is unknown.
    /// </remarks>
    public bool AcceptsAge(int? age) =>
        age is not { } years ||
        ((MinimumAge is null || years >= MinimumAge) &&
         (MaximumAge is null || years <= MaximumAge));

    /// <summary>
    /// Adds a weekly slot, and returns it. A class may meet more than once a week.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The new slot overlaps one this class already has.
    /// </exception>
    public MeetingPattern AddMeetingPattern(DayOfWeek dayOfWeek, TimeOnly startTime, TimeSpan duration)
    {
        var pattern = new MeetingPattern(Id, dayOfWeek, startTime, duration) { StudioId = StudioId };

        var clashing = _meetingPatterns.FirstOrDefault(p => p.OverlapsWith(pattern));

        if (clashing is not null)
        {
            // A class overlapping *itself* is an invariant, not a conflict: it needs no other
            // rows to detect, so it belongs here rather than in conflict detection, which is
            // reserved for combinations that are only wrong across entities.
            throw new InvalidOperationException(
                $"\"{Name}\" already meets {clashing}, which overlaps {pattern}.");
        }

        _meetingPatterns.Add(pattern);

        return pattern;
    }

    /// <summary>
    /// Assigns a member to teach this class in the given capacity, and returns the assignment.
    /// </summary>
    /// <param name="from">Defaults to the class start date.</param>
    /// <param name="until">Defaults to open-ended, which means "until the class ends".</param>
    /// <exception cref="InvalidOperationException">
    /// The assignment falls outside the class's dates, or duplicates an overlapping assignment
    /// of the same member in the same capacity.
    /// </exception>
    public ClassAssignment Assign(Member member, AssignmentRole role,
        DateOnly? from = null, DateOnly? until = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        GuardSameStudio(member);

        var start = from ?? StartDate;

        if (!RunsOn(start) || (until is { } end && !RunsOn(end)))
        {
            throw new InvalidOperationException(
                $"Assignment dates ({start} to {until?.ToString() ?? "open-ended"}) must fall " +
                $"within \"{Name}\"'s own dates ({StartDate} to {EndDate}).");
        }

        var assignment = new ClassAssignment(Id, member.Id, role, start, until)
        {
            StudioId = StudioId
        };

        var duplicate = _assignments.FirstOrDefault(a =>
            a.MemberId == member.Id &&
            a.Role == role &&
            a.OverlapsWith(assignment));

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"{member.FullName} is already assigned to \"{Name}\" as {role} " +
                $"from {duplicate.From}, which overlaps the requested period.");
        }

        _assignments.Add(assignment);

        return assignment;
    }

    /// <summary>Members teaching this class on <paramref name="asOf"/>, in any capacity.</summary>
    public IEnumerable<ClassAssignment> AssignmentsOn(DateOnly asOf) =>
        _assignments.Where(a => a.IsActiveOn(asOf));

    public void UpdateDetails(string name, string style, string? level, int capacity,
        int? minimumAge, int? maximumAge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        if (minimumAge is { } lower && maximumAge is { } upper && lower > upper)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAge),
                $"Minimum age ({lower}) cannot exceed maximum age ({upper}).");
        }

        Name = name.Trim();
        Style = style.Trim();
        Level = string.IsNullOrWhiteSpace(level) ? null : level.Trim();
        Capacity = capacity;
        MinimumAge = minimumAge;
        MaximumAge = maximumAge;
    }

    public void MoveTo(Room? room)
    {
        GuardSameStudio(room);
        RoomId = room?.Id;
    }

    /// <summary>
    /// Rejects references that cross a tenant boundary.
    /// </summary>
    /// <remarks>
    /// Necessarily partial, and worth being honest about: it can only compare tenants that are
    /// already known, and an entity constructed in this same unit of work has an unset
    /// <see cref="ITenantOwned.StudioId"/> until the insert interceptor stamps it on save. So
    /// this catches the case that actually happens in production — an entity loaded from one
    /// studio being attached to another — and cannot catch a purely in-memory graph that has
    /// never been saved. The real backstop is the query filter, which makes a foreign studio's
    /// rows unreachable to load in the first place.
    /// </remarks>
    private void GuardSameStudio(params ITenantOwned?[] others)
    {
        foreach (var other in others)
        {
            if (other is null || other.StudioId == Guid.Empty || StudioId == Guid.Empty)
            {
                continue;
            }

            if (other.StudioId != StudioId)
            {
                throw new InvalidOperationException(
                    $"Cannot reference a {other.GetType().Name} belonging to studio " +
                    $"{other.StudioId} from a class belonging to studio {StudioId}.");
            }
        }
    }
}
