namespace Pirouette.Domain.Tests;

public class HouseholdTests
{
    [Fact]
    public void Constructor_SetsNameAndContactDetails()
    {
        var household = new Household(
            "The Okonkwo Family",
            addressLine1: "42 Rosewood Avenue",
            city: "Brookline",
            region: "MA",
            postalCode: "02445",
            primaryPhone: "617-555-0142",
            primaryEmail: "okonkwo.family@example.com");

        Assert.Equal("The Okonkwo Family", household.Name);
        Assert.Equal("Brookline", household.City);
        Assert.NotEqual(Guid.Empty, household.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankName_Throws(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Household(name!));
    }

    [Fact]
    public void Constructor_WithNoContactDetails_IsAllowed()
    {
        // A household created from a registration form that only captured a name is
        // incomplete, not invalid. Requiring an address here would block the common path of
        // entering a family first and chasing details later.
        var household = new Household("The Okonkwo Family");

        Assert.Null(household.AddressLine1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_CollapsesBlankFieldsToNull(string blank)
    {
        // A blank string and a null both mean "not provided". Allowing both would make every
        // downstream check test for two things, and would let the database store a value
        // indistinguishable from a real one.
        var household = new Household("The Okonkwo Family", addressLine1: blank, city: blank);

        Assert.Null(household.AddressLine1);
        Assert.Null(household.City);
    }

    [Fact]
    public void Constructor_TrimsFields()
    {
        var household = new Household("  The Okonkwo Family  ", addressLine1: "  42 Rosewood Avenue  ");

        Assert.Equal("The Okonkwo Family", household.Name);
        Assert.Equal("42 Rosewood Avenue", household.AddressLine1);
    }

    [Fact]
    public void UpdateContactDetails_ReplacesEveryField()
    {
        var household = new Household("The Okonkwo Family", addressLine1: "42 Rosewood Avenue");

        household.UpdateContactDetails("9 Elm Street", "Newton", "MA", "02458", null, null);

        Assert.Equal("9 Elm Street", household.AddressLine1);
        Assert.Equal("Newton", household.City);
        Assert.Null(household.PrimaryPhone);
    }
}
