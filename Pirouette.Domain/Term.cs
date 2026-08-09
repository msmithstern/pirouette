namespace Pirouette.Domain;

/// <summary>
/// A teaching period such as "Fall 2026". Classes belong to a term, and a term bounds the
/// dates for which sessions can be generated.
/// </summary>
public sealed class Term : ITenantOwned
{
    /// <summary>Constructor used only by EF Core. See <see cref="Studio"/> for why.</summary>
    private Term() { }

    public Term(string name, DateOnly startDate, DateOnly endDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (endDate < startDate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endDate),
                $"Term end date ({endDate}) cannot be before its start date ({startDate}).");
        }

        Id = Guid.CreateVersion7();
        Name = name.Trim();
        StartDate = startDate;
        EndDate = endDate;
    }

    public Guid Id { get; private set; }

    public Guid StudioId { get; set; }

    public string Name { get; private set; } = null!;

    /// <summary>
    /// Dates, not instants. A term begins on a day; it has no meaningful time-of-day component,
    /// and modelling it as <see cref="DateTime"/> would invite time zone bugs for no benefit.
    /// </summary>
    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    /// <summary>Inclusive of both endpoints, so a single-day term has a length of 1.</summary>
    public int LengthInDays => EndDate.DayNumber - StartDate.DayNumber + 1;

    /// <summary>
    /// Whether <paramref name="date"/> falls within this term, inclusive of both endpoints.
    /// </summary>
    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;
}
