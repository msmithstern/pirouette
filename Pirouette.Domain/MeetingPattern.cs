namespace Pirouette.Domain;

/// <summary>
/// One weekly slot a class occupies, e.g. "Tuesdays, 16:00, for one hour". A class may have
/// several, or none at all.
/// </summary>
/// <remarks>
/// <b>A pattern is a generator, not a constraint.</b> It exists to emit dated
/// <c>ClassSession</c> rows; once a session exists it is an independent fact that can be
/// cancelled, moved, or reassigned without the pattern's permission. A class with no patterns
/// is therefore not broken — it is a workshop series whose dates are all entered by hand.
///
/// <para><b>Why duration and not an end time.</b> Storing <c>StartTime</c> and <c>EndTime</c>
/// and validating that one follows the other means a backwards class is a state the type
/// admits and a check rejects. Storing a start and a duration makes it a state that cannot be
/// written down at all: there is no pair of values here that describes a class ending before
/// it begins. Unrepresentable beats validated — this is the clearest instance of it in the
/// model, which is why it drove the shape of the type.</para>
///
/// <para><b>Why <see cref="System.DayOfWeek"/> and <see cref="TimeOnly"/>, not
/// <see cref="DateTime"/>.</b> "Tuesdays at 4pm" is a wall-clock fact and must stay 4pm across
/// a daylight-saving transition. A UTC instant on the definition would silently move the class
/// to 3pm in November.</para>
/// </remarks>
public sealed class MeetingPattern : ITenantOwned
{
    /// <summary>
    /// Longest permitted single meeting. A sanity bound rather than a business rule: it exists
    /// to catch a duration entered in the wrong unit, where 90 becomes 90 hours rather than 90
    /// minutes. Set well above any real class so it never rejects legitimate data — a
    /// full-day intensive still fits.
    /// </summary>
    public static readonly TimeSpan MaximumDuration = TimeSpan.FromHours(8);

    /// <summary>Constructor used only by EF Core. See <see cref="Studio"/> for why.</summary>
    private MeetingPattern() { }

    internal MeetingPattern(Guid danceClassId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"A class must last longer than zero (got {duration}).");
        }

        if (duration > MaximumDuration)
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"A single meeting cannot run longer than {MaximumDuration:%h} hours (got {duration}).");
        }

        // Rejected rather than allowed to wrap. A dance studio does not run a class through
        // midnight, so a start plus duration that passes it is a data entry error — and if it
        // silently wrapped, the session generated for it would appear on the wrong day, which
        // is far harder to notice than a rejected form.
        if (startTime.ToTimeSpan() + duration > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(duration),
                $"A class starting at {startTime} and running for {duration} would cross midnight.");
        }

        Id = Guid.CreateVersion7();
        DanceClassId = danceClassId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        Duration = duration;
    }

    public Guid Id { get; private set; }

    public Guid StudioId { get; set; }

    public Guid DanceClassId { get; private set; }

    /// <summary>
    /// Persisted as its integer value, unlike <see cref="StudioRole"/>. The BCL fixes these
    /// numbers (Sunday is 0) and cannot renumber them without breaking every .NET program ever
    /// written, so the reordering risk that makes string storage worthwhile for our own enums
    /// does not exist here.
    /// </summary>
    public DayOfWeek DayOfWeek { get; private set; }

    /// <summary>Wall-clock start time in the studio's timezone.</summary>
    public TimeOnly StartTime { get; private set; }

    public TimeSpan Duration { get; private set; }

    /// <summary>
    /// Computed, never stored. Guaranteed to be after <see cref="StartTime"/> on the same day,
    /// because the constructor rejects anything that would cross midnight.
    /// </summary>
    public TimeOnly EndTime => StartTime.Add(Duration);

    /// <summary>
    /// The first date on or after <paramref name="onOrAfter"/> that falls on this pattern's day
    /// of the week. The starting point for expanding a pattern across a term.
    /// </summary>
    public DateOnly FirstOccurrenceOnOrAfter(DateOnly onOrAfter)
    {
        // Modulo 7 of a difference that may be negative: adding 7 before the second modulo
        // keeps the result in [0, 6] rather than returning a negative offset that would walk
        // the date backwards.
        var daysAhead = ((int)DayOfWeek - (int)onOrAfter.DayOfWeek + 7) % 7;

        return onOrAfter.AddDays(daysAhead);
    }

    /// <summary>Whether this pattern and <paramref name="other"/> overlap in wall-clock time.</summary>
    /// <remarks>
    /// Same-day, half-open intervals: a class ending at 17:00 does not clash with one starting
    /// at 17:00. Room changeover buffers are a separate concern layered on top later, not
    /// baked in here.
    /// </remarks>
    public bool OverlapsWith(MeetingPattern other) =>
        DayOfWeek == other.DayOfWeek &&
        StartTime < other.EndTime &&
        other.StartTime < EndTime;

    public override string ToString() => $"{DayOfWeek}s {StartTime:HH:mm}–{EndTime:HH:mm}";
}
