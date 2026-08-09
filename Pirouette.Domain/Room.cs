namespace Pirouette.Domain;

/// <summary>
/// A physical space in which classes are held. Capacity feeds the over-enrolment check and
/// room double-booking detection.
/// </summary>
public sealed class Room : ITenantOwned
{
    /// <summary>Constructor used only by EF Core. See <see cref="Studio"/> for why.</summary>
    private Room() { }

    public Room(string name, int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        Id = Guid.CreateVersion7();
        Name = name.Trim();
        Capacity = capacity;
    }

    public Guid Id { get; private set; }

    public Guid StudioId { get; set; }

    public string Name { get; private set; } = null!;

    /// <summary>Maximum number of dancers the room can hold. Always at least 1.</summary>
    public int Capacity { get; private set; }
}
