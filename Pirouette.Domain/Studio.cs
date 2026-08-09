namespace Pirouette.Domain;

/// <summary>
/// A dance studio. The tenant root: every other entity belongs to exactly one of these.
/// </summary>
public sealed class Studio
{
    /// <summary>Constructor used only by EF Core when materialising rows from the database.</summary>
    /// <remarks>
    /// Data already persisted has already satisfied these invariants, and throwing during a
    /// read would be both wasteful and impossible to recover from. EF finds this by convention.
    /// </remarks>
    private Studio() { }

    public Studio(string name, string timeZoneId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);

        // Throws TimeZoneNotFoundException for an unrecognised id. .NET accepts IANA ids on
        // every platform since .NET 6, so "America/New_York" resolves on Windows too.
        _ = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        Id = Guid.CreateVersion7();
        Name = name.Trim();
        TimeZoneId = timeZoneId;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    /// <summary>
    /// IANA time zone identifier, e.g. "America/New_York".
    /// </summary>
    /// <remarks>
    /// Stored as an id rather than a UTC offset on purpose: offsets change twice a year with
    /// daylight saving, zone ids do not. A class scheduled for "Tuesdays at 4pm" must stay at
    /// 4pm across the transition, which is only possible if the zone is known.
    /// </remarks>
    public string TimeZoneId { get; private set; } = null!;

    public TimeZoneInfo TimeZone => TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
}
