namespace Pirouette.Domain.Tests;

public class TermTests
{
    private static readonly DateOnly Sept1 = new(2026, 9, 1);
    private static readonly DateOnly Dec15 = new(2026, 12, 15);

    [Fact]
    public void Constructor_WithValidRange_SetsDates()
    {
        var term = new Term("Fall 2026", Sept1, Dec15);

        Assert.Equal("Fall 2026", term.Name);
        Assert.Equal(Sept1, term.StartDate);
        Assert.Equal(Dec15, term.EndDate);
        Assert.NotEqual(Guid.Empty, term.Id);
    }

    [Fact]
    public void Constructor_WhenEndBeforeStart_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Term("Backwards", Dec15, Sept1));
    }

    [Fact]
    public void Constructor_WhenEndEqualsStart_IsAllowed()
    {
        // A one-day term is legitimate: a masterclass or an intensive. This is the boundary
        // that distinguishes `end < start` from `end <= start`, and getting it wrong would
        // silently reject valid data.
        var term = new Term("One-day intensive", Sept1, Sept1);

        Assert.Equal(1, term.LengthInDays);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        // ThrowsAny, not Throws: xUnit's Assert.Throws<T> matches the exception type exactly,
        // and ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for null
        // but ArgumentException for empty and whitespace. Exact matching would fail on the
        // null case only — the kind of thing that looks like a flaky test.
        Assert.ThrowsAny<ArgumentException>(() => new Term(name!, Sept1, Dec15));
    }

    [Fact]
    public void Name_IsTrimmed()
    {
        Assert.Equal("Fall 2026", new Term("  Fall 2026  ", Sept1, Dec15).Name);
    }

    [Fact]
    public void LengthInDays_IsInclusiveOfBothEndpoints()
    {
        var term = new Term("Three days", Sept1, Sept1.AddDays(2));

        Assert.Equal(3, term.LengthInDays);
    }

    [Theory]
    [InlineData(2026, 9, 1, true)]    // first day
    [InlineData(2026, 12, 15, true)]  // last day
    [InlineData(2026, 10, 13, true)]  // middle
    [InlineData(2026, 8, 31, false)]  // day before
    [InlineData(2026, 12, 16, false)] // day after
    public void Contains_IsInclusiveOfBothEndpoints(int year, int month, int day, bool expected)
    {
        var term = new Term("Fall 2026", Sept1, Dec15);

        Assert.Equal(expected, term.Contains(new DateOnly(year, month, day)));
    }
}
