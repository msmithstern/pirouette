namespace Pirouette.Domain.Tests;

/// <summary>
/// Meeting patterns are only constructible through <see cref="DanceClass.AddMeetingPattern"/>,
/// so these tests go through a class rather than calling the internal constructor. That is the
/// point: there is no way for application code to build one that skips the checks.
/// </summary>
public class MeetingPatternTests
{
    private static readonly TimeOnly FourPm = new(16, 0);

    private static DanceClass AClass()
    {
        var term = new Term("Fall 2026", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 15));

        return new DanceClass(term, "Intermediate Ballet", "Ballet", capacity: 14);
    }

    private static MeetingPattern APattern(TimeOnly start, TimeSpan duration,
        DayOfWeek day = DayOfWeek.Tuesday) =>
        AClass().AddMeetingPattern(day, start, duration);

    [Fact]
    public void Constructor_SetsTheSlot()
    {
        var pattern = APattern(FourPm, TimeSpan.FromHours(1));

        Assert.Equal(DayOfWeek.Tuesday, pattern.DayOfWeek);
        Assert.Equal(FourPm, pattern.StartTime);
        Assert.Equal(TimeSpan.FromHours(1), pattern.Duration);
    }

    [Fact]
    public void EndTime_IsComputedFromTheDuration()
    {
        // There is no EndTime column, and that is deliberate: a class that ends before it
        // begins is not merely rejected here, it is unrepresentable. No pair of values this
        // type accepts can describe one.
        var pattern = APattern(FourPm, TimeSpan.FromMinutes(90));

        Assert.Equal(new TimeOnly(17, 30), pattern.EndTime);
    }

    [Fact]
    public void Constructor_WithZeroDuration_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => APattern(FourPm, TimeSpan.Zero));
    }

    [Fact]
    public void Constructor_WithNegativeDuration_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => APattern(FourPm, TimeSpan.FromHours(-1)));
    }

    [Fact]
    public void Constructor_AtExactlyTheMaximumDuration_IsAllowed()
    {
        // The boundary that separates the sanity bound from a real full-day intensive.
        var pattern = APattern(new TimeOnly(9, 0), MeetingPattern.MaximumDuration);

        Assert.Equal(new TimeOnly(17, 0), pattern.EndTime);
    }

    [Fact]
    public void Constructor_BeyondTheMaximumDuration_Throws()
    {
        // Catches a duration entered in the wrong unit — 90 meaning 90 hours, not 90 minutes.
        var tooLong = MeetingPattern.MaximumDuration + TimeSpan.FromMinutes(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => APattern(new TimeOnly(0, 0), tooLong));
    }

    [Fact]
    public void Constructor_WhenTheClassWouldCrossMidnight_Throws()
    {
        // Rejected rather than allowed to wrap. A wrapped time would generate sessions on the
        // wrong day, which is far harder to spot than a rejected form.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => APattern(new TimeOnly(23, 0), TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Constructor_EndingExactlyAtMidnight_IsAllowed()
    {
        var pattern = APattern(new TimeOnly(22, 0), TimeSpan.FromHours(2));

        Assert.Equal(new TimeOnly(0, 0), pattern.EndTime);
    }

    // --- Expansion ---------------------------------------------------------------------

    [Theory]
    [InlineData(2026, 9, 1, 2026, 9, 1)]  // a Tuesday: returns the same day, not next week
    [InlineData(2026, 9, 2, 2026, 9, 8)]  // a Wednesday: wraps to the following Tuesday
    [InlineData(2026, 8, 31, 2026, 9, 1)] // a Monday: the next day
    [InlineData(2026, 9, 6, 2026, 9, 8)]  // a Sunday: the modulo case that goes negative
    public void FirstOccurrenceOnOrAfter_FindsTheRightDate(
        int fromYear, int fromMonth, int fromDay,
        int expectedYear, int expectedMonth, int expectedDay)
    {
        var pattern = APattern(FourPm, TimeSpan.FromHours(1));

        var actual = pattern.FirstOccurrenceOnOrAfter(new DateOnly(fromYear, fromMonth, fromDay));

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), actual);
    }

    // --- Overlap -----------------------------------------------------------------------

    [Fact]
    public void OverlapsWith_IgnoresDifferentDays()
    {
        var tuesday = APattern(FourPm, TimeSpan.FromHours(1));
        var thursday = APattern(FourPm, TimeSpan.FromHours(1), DayOfWeek.Thursday);

        Assert.False(tuesday.OverlapsWith(thursday));
    }

    [Fact]
    public void OverlapsWith_IsFalseWhenOneEndsAsTheOtherBegins()
    {
        // Half-open intervals: 16:00–17:00 does not clash with 17:00–18:00. A changeover
        // buffer is a separate concern and is deliberately not baked in here.
        var earlier = APattern(FourPm, TimeSpan.FromHours(1));
        var later = APattern(new TimeOnly(17, 0), TimeSpan.FromHours(1));

        Assert.False(earlier.OverlapsWith(later));
        Assert.False(later.OverlapsWith(earlier));
    }

    [Fact]
    public void OverlapsWith_IsTrueForAPartialOverlap()
    {
        var earlier = APattern(FourPm, TimeSpan.FromHours(1));
        var later = APattern(new TimeOnly(16, 30), TimeSpan.FromHours(1));

        Assert.True(earlier.OverlapsWith(later));
        Assert.True(later.OverlapsWith(earlier));
    }

    [Fact]
    public void OverlapsWith_IsTrueWhenOneContainsTheOther()
    {
        var outer = APattern(FourPm, TimeSpan.FromHours(3));
        var inner = APattern(new TimeOnly(17, 0), TimeSpan.FromMinutes(30));

        Assert.True(outer.OverlapsWith(inner));
        Assert.True(inner.OverlapsWith(outer));
    }
}
