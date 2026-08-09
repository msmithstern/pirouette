namespace Pirouette.Domain.Tests;

public class MemberTests
{
    private static readonly DateOnly Sept2024 = new(2024, 9, 1);
    private static readonly DateOnly Sept2026 = new(2026, 9, 1);
    private static readonly DateOnly June2026 = new(2026, 6, 30);

    private static Member AMember() => new("Nadia", "Okonkwo", new DateOnly(2010, 3, 14));

    [Fact]
    public void Constructor_SetsIdentity()
    {
        var member = AMember();

        Assert.Equal("Nadia", member.FirstName);
        Assert.Equal("Okonkwo", member.LastName);
        Assert.Equal("Nadia Okonkwo", member.FullName);
        Assert.NotEqual(Guid.Empty, member.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankFirstName_Throws(string? firstName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Member(firstName!, "Okonkwo"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankLastName_Throws(string? lastName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Member("Nadia", lastName!));
    }

    [Fact]
    public void Constructor_WithFutureDateOfBirth_Throws()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Member("Nadia", "Okonkwo", tomorrow));
    }

    [Fact]
    public void Constructor_WithNoDateOfBirth_IsAllowed()
    {
        // An adult in a drop-in tap class has no reason to give one, and a member with no
        // birthday on file is a normal state rather than incomplete data.
        var member = new Member("Ade", "Okonkwo");

        Assert.Null(member.DateOfBirth);
        Assert.Null(member.AgeOn(Sept2026));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_CollapsesBlankContactDetailsToNull(string blank)
    {
        var member = new Member("Nadia", "Okonkwo", email: blank, phone: blank);

        Assert.Null(member.Email);
        Assert.Null(member.Phone);
    }

    [Fact]
    public void Constructor_TrimsNames()
    {
        var member = new Member("  Nadia  ", "  Okonkwo  ");

        Assert.Equal("Nadia Okonkwo", member.FullName);
    }

    // --- Age ---------------------------------------------------------------------------

    [Theory]
    [InlineData(2026, 3, 13, 15)] // day before the birthday
    [InlineData(2026, 3, 14, 16)] // on the birthday
    [InlineData(2026, 3, 15, 16)] // day after
    public void AgeOn_CountsCompletedYears(int year, int month, int day, int expected)
    {
        var member = AMember(); // born 2010-03-14

        Assert.Equal(expected, member.AgeOn(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData(2026, 2, 27, 17)] // two days short in a non-leap year
    [InlineData(2026, 2, 28, 18)] // treated as the birthday, since 29 February does not exist
    [InlineData(2026, 3, 1, 18)]
    [InlineData(2028, 2, 28, 19)] // a leap year, so 29 February is a real date and not yet reached
    [InlineData(2028, 2, 29, 20)]
    public void AgeOn_HandlesALeapDayBirthday(int year, int month, int day, int expected)
    {
        // Someone born on 29 February has no birthday in three years out of four, so the
        // calculation has to pick a convention. DateOnly.AddYears clamps to 28 February in a
        // non-leap year, which means they age up on the 28th — a defensible choice, and more
        // to the point a consistent one. A hand-rolled month/day comparison gets this wrong
        // and nobody notices for four years, which is why it is pinned by a test.
        var leapling = new Member("Ada", "Leap", new DateOnly(2008, 2, 29));

        Assert.Equal(expected, leapling.AgeOn(new DateOnly(year, month, day)));
    }

    [Fact]
    public void AgeOn_BeforeBirth_IsNegative()
    {
        // Not guarded against: asking for someone's age before they were born is a caller bug,
        // and returning a nonsense number the caller can see beats silently clamping to zero,
        // which would look like a real infant.
        var member = AMember();

        Assert.True(member.AgeOn(new DateOnly(2009, 1, 1)) < 0);
    }

    // --- Roles -------------------------------------------------------------------------

    [Fact]
    public void GrantRole_AddsATimeBoundedRole()
    {
        var member = AMember();

        var role = member.GrantRole(StudioRole.Student, Sept2024);

        Assert.Equal(StudioRole.Student, role.Role);
        Assert.Equal(Sept2024, role.From);
        Assert.Null(role.Until);
        Assert.Single(member.Roles);
    }

    [Fact]
    public void HasRole_IsAwareOfTime()
    {
        var member = AMember();
        member.GrantRole(StudioRole.Student, Sept2024, June2026);

        Assert.True(member.HasRole(StudioRole.Student, new DateOnly(2025, 1, 1)));
        Assert.False(member.HasRole(StudioRole.Student, new DateOnly(2024, 1, 1)));
        Assert.False(member.HasRole(StudioRole.Student, new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void HasRole_IsInclusiveOfBothBounds()
    {
        var member = AMember();
        member.GrantRole(StudioRole.Student, Sept2024, June2026);

        Assert.True(member.HasRole(StudioRole.Student, Sept2024));
        Assert.True(member.HasRole(StudioRole.Student, June2026));
    }

    [Fact]
    public void GrantRole_WithOverlappingPeriod_Throws()
    {
        var member = AMember();
        member.GrantRole(StudioRole.Student, Sept2024);

        // Two simultaneous identical roles are not a fact about the world, they are a
        // double-submitted form — and they make "when did she become a student?" ambiguous.
        Assert.Throws<InvalidOperationException>(
            () => member.GrantRole(StudioRole.Student, Sept2026));
    }

    [Fact]
    public void GrantRole_WithNonOverlappingPeriod_IsAllowed()
    {
        // A student who leaves and returns is ordinary. Rejecting this would force the studio
        // to either lie about the dates or lose the gap.
        var member = AMember();
        member.GrantRole(StudioRole.Student, Sept2024, new DateOnly(2025, 6, 30));

        member.GrantRole(StudioRole.Student, Sept2026);

        Assert.Equal(2, member.Roles.Count);
    }

    [Fact]
    public void GrantRole_WithEndBeforeStart_Throws()
    {
        var member = AMember();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => member.GrantRole(StudioRole.Student, Sept2026, Sept2024));
    }

    [Fact]
    public void Roles_CannotBeMutatedFromOutside()
    {
        // The collection is exposed read-only because adding to it directly would bypass the
        // overlap check, which is the only thing keeping the role history unambiguous.
        var member = AMember();
        var existing = member.GrantRole(StudioRole.Student, Sept2024);

        Assert.Throws<NotSupportedException>(
            () => ((ICollection<MemberRole>)member.Roles).Add(existing));
    }

    // --- The student-teacher -----------------------------------------------------------

    [Fact]
    public void AMemberCanBeBothAStudentAndStaffAtOnce()
    {
        // The case that forced one Member per human instead of separate Student and Instructor
        // entities. A 16-year-old assisting the beginner class is one row holding two roles —
        // no duplicate person, no two birthdays to keep in sync.
        var nadia = AMember();
        nadia.GrantRole(StudioRole.Student, Sept2024);
        nadia.GrantRole(StudioRole.AssistantStaff, Sept2026);

        var whileStillOnlyAStudent = new DateOnly(2025, 1, 1);
        var afterBecomingAnAssistant = new DateOnly(2026, 10, 1);

        Assert.True(nadia.HasRole(StudioRole.Student, whileStillOnlyAStudent));
        Assert.False(nadia.IsStaffOn(whileStillOnlyAStudent));

        Assert.True(nadia.HasRole(StudioRole.Student, afterBecomingAnAssistant));
        Assert.True(nadia.IsStaffOn(afterBecomingAnAssistant));
    }

    [Theory]
    [InlineData(StudioRole.Owner, true)]
    [InlineData(StudioRole.Admin, true)]
    [InlineData(StudioRole.Staff, true)]
    [InlineData(StudioRole.AssistantStaff, true)]
    [InlineData(StudioRole.Student, false)]
    [InlineData(StudioRole.Guardian, false)]
    public void IsStaffOn_CoversEveryRole(StudioRole role, bool expected)
    {
        var member = AMember();
        member.GrantRole(role, Sept2024);

        Assert.Equal(expected, member.IsStaffOn(Sept2026));
    }

    [Fact]
    public void EndOn_ClosesARoleWithoutErasingItsHistory()
    {
        // "She stopped teaching in June" and "she never taught" are different facts, and only
        // the first can answer a question about last term's rosters.
        var member = AMember();
        var role = member.GrantRole(StudioRole.Staff, Sept2024);

        role.EndOn(June2026);

        Assert.True(member.IsStaffOn(new DateOnly(2025, 1, 1)));
        Assert.False(member.IsStaffOn(Sept2026));
    }

    [Fact]
    public void EndOn_BeforeTheRoleBegan_Throws()
    {
        var member = AMember();
        var role = member.GrantRole(StudioRole.Staff, Sept2026);

        Assert.Throws<ArgumentOutOfRangeException>(() => role.EndOn(Sept2024));
    }

    // --- Households --------------------------------------------------------------------

    [Fact]
    public void AssignToHousehold_LinksAndUnlinks()
    {
        var household = new Household("The Okonkwo Family");
        var member = AMember();

        member.AssignToHousehold(household);
        Assert.Equal(household.Id, member.HouseholdId);

        member.AssignToHousehold(null);
        Assert.Null(member.HouseholdId);
    }
}
