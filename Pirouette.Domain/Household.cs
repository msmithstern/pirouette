namespace Pirouette.Domain;

/// <summary>
/// A billing unit — typically a family. Members may belong to one, and siblings sharing a
/// household is what makes multi-child discounts expressible later.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="Member"/> rather than duplicating an address onto
/// every family member. Three siblings with three copies of the same address means three
/// places to update when the family moves, and no way to tell whether a mismatch is a typo or
/// a genuine second residence.
///
/// <para>Membership is optional. An adult taking a tap class is their own billing party and
/// does not need a household row invented for them.</para>
/// </remarks>
public sealed class Household : ITenantOwned
{
    /// <summary>Constructor used only by EF Core. See <see cref="Studio"/> for why.</summary>
    private Household() { }

    public Household(string name, string? addressLine1 = null, string? city = null,
        string? region = null, string? postalCode = null,
        string? primaryPhone = null, string? primaryEmail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = Guid.CreateVersion7();
        Name = name.Trim();
        AddressLine1 = Normalise(addressLine1);
        City = Normalise(city);
        Region = Normalise(region);
        PostalCode = Normalise(postalCode);
        PrimaryPhone = Normalise(primaryPhone);
        PrimaryEmail = Normalise(primaryEmail);
    }

    public Guid Id { get; private set; }

    public Guid StudioId { get; set; }

    /// <summary>
    /// How the studio refers to the household, e.g. "The Okonkwo Family".
    /// </summary>
    /// <remarks>
    /// Not derived from the members' surnames. Blended families, hyphenated names, and
    /// guardians with a different surname to the child all make derivation wrong often enough
    /// that a plain editable label is the more honest model.
    /// </remarks>
    public string Name { get; private set; } = null!;

    public string? AddressLine1 { get; private set; }

    public string? City { get; private set; }

    /// <summary>State, province, or county. Named generically because the studio may not be in the US.</summary>
    public string? Region { get; private set; }

    public string? PostalCode { get; private set; }

    public string? PrimaryPhone { get; private set; }

    public string? PrimaryEmail { get; private set; }

    public void UpdateContactDetails(string? addressLine1, string? city, string? region,
        string? postalCode, string? primaryPhone, string? primaryEmail)
    {
        AddressLine1 = Normalise(addressLine1);
        City = Normalise(city);
        Region = Normalise(region);
        PostalCode = Normalise(postalCode);
        PrimaryPhone = Normalise(primaryPhone);
        PrimaryEmail = Normalise(primaryEmail);
    }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    /// <summary>
    /// Trims, and collapses whitespace-only input to null.
    /// </summary>
    /// <remarks>
    /// A blank string and a null both mean "not provided", and allowing both means every
    /// downstream check has to test for two things. Empty-as-null is chosen over
    /// null-as-empty because the database can then express "unknown" with NULL rather than
    /// with a sentinel indistinguishable from a real value.
    /// </remarks>
    private static string? Normalise(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
