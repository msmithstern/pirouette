namespace Pirouette.Domain.Tests;

public class RoomTests
{
    [Fact]
    public void Constructor_WithValidValues_SetsProperties()
    {
        var room = new Room("Studio A", 24);

        Assert.Equal("Studio A", room.Name);
        Assert.Equal(24, room.Capacity);
        Assert.NotEqual(Guid.Empty, room.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithNonPositiveCapacity_Throws(int capacity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Room("Studio A", capacity));
    }

    [Fact]
    public void Constructor_WithCapacityOfOne_IsAllowed()
    {
        // Private lessons happen. The boundary is 0, not 1.
        Assert.Equal(1, new Room("Practice Room", 1).Capacity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        // ThrowsAny: null produces ArgumentNullException, blank produces ArgumentException.
        Assert.ThrowsAny<ArgumentException>(() => new Room(name!, 10));
    }
}
