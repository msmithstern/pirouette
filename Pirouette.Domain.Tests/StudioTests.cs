namespace Pirouette.Domain.Tests;

public class StudioTests
{
    [Fact]
    public void Constructor_WithValidValues_SetsProperties()
    {
        var studio = new Studio("Pirouette Dance Academy", "America/New_York");

        Assert.Equal("Pirouette Dance Academy", studio.Name);
        Assert.Equal("America/New_York", studio.TimeZoneId);
        Assert.NotEqual(Guid.Empty, studio.Id);
    }

    [Fact]
    public void Constructor_WithUnknownTimeZone_Throws()
    {
        Assert.Throws<TimeZoneNotFoundException>(
            () => new Studio("Pirouette", "Middle_Earth/Rivendell"));
    }

    [Fact]
    public void TimeZone_ResolvesToUsableTimeZoneInfo()
    {
        var studio = new Studio("Pirouette", "America/New_York");

        Assert.NotNull(studio.TimeZone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        // ThrowsAny: null produces ArgumentNullException, blank produces ArgumentException.
        Assert.ThrowsAny<ArgumentException>(() => new Studio(name!, "America/New_York"));
    }
}
