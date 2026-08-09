namespace Pirouette.Domain.Tests;

public class DanceClassTests
{
    private static readonly DateOnly TermStart = new(2026, 9, 1);
    private static readonly DateOnly TermEnd = new(2026, 12, 15);
    private static readonly TimeOnly FourPm = new(16, 0);
    private static readonly TimeSpan AnHour = TimeSpan.FromHours(1);

    private static Term ATerm() => new("Fall 2026", TermStart, TermEnd);

    private static DanceClass AClass(Term? term = null, Room? room = null,
        int? minimumAge = null, int? maximumAge = null,
        DateOnly? startDate = null, DateOnly? endDate = null) =>
        new(term ?? ATerm(), "Intermediate Ballet", "Ballet", capacity: 14, room: room,
            level: "Level 3", minimumAge: minimumAge, maximumAge: maximumAge,
            startDate: startDate, endDate: endDate);

    // --- Construction ------------------------------------------------------------------

    [Fact]
    public void Constructor_DefaultsDatesToTheTerm()
    {
        var danceClass = AClass();

        Assert.Equal(TermStart, danceClass.StartDate);
        Assert.Equal(TermEnd, danceClass.EndDate);
    }

    [Fact]
    public void Constructor_AllowsAShorterRunInsideTheTerm()
    {
        // A six-week workshop inside a sixteen-week term is an ordinary thing to want.
        var danceClass = AClass(
            startDate: new DateOnly(2026, 10, 1),
            endDate: new DateOnly(2026, 11, 12));

        Assert.Equal(new DateOnly(2026, 10, 1), danceClass.StartDate);
    }

    [Theory]
    [InlineData(2026, 8, 31)] // the day before the term
    [InlineData(2026, 12, 16)] // the day after
    public void Constructor_WithDatesOutsideTheTerm_Throws(int year, int month, int day)
    {
        var outside = new DateOnly(year, month, day);

        Assert.Throws<ArgumentOutOfRangeException>(() => AClass(startDate: outside, endDate: outside));
    }

    [Fact]
    public void Constructor_WithEndBeforeStart_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AClass(startDate: new DateOnly(2026, 11, 1), endDate: new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Constructor_WithZeroCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DanceClass(ATerm(), "Ballet", "Ballet", capacity: 0));
    }

    [Fact]
    public void Constructor_WithMinimumAgeAboveMaximum_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AClass(minimumAge: 12, maximumAge: 8));
    }

    [Fact]
    public void Constructor_WithEqualMinimumAndMaximumAge_IsAllowed()
    {
        // A single-age class is legitimate, so the boundary is `>` and not `>=`.
        var danceClass = AClass(minimumAge: 8, maximumAge: 8);

        Assert.True(danceClass.AcceptsAge(8));
    }

    [Fact]
    public void Constructor_InheritsTheStudioFromItsTerm()
    {
        var term = ATerm();
        term.StudioId = Guid.NewGuid();

        Assert.Equal(term.StudioId, AClass(term).StudioId);
    }

    [Fact]
    public void Constructor_WithARoomFromAnotherStudio_Throws()
    {
        // The cross-tenant trap. Query filters make a foreign studio's rows unloadable in the
        // first place, so this is a backstop rather than the primary defence — but a backstop
        // that fires in memory beats one that fires as a foreign key violation.
        var term = ATerm();
        term.StudioId = Guid.NewGuid();

        var foreignRoom = new Room("Studio A", 20) { StudioId = Guid.NewGuid() };

        Assert.Throws<InvalidOperationException>(() => AClass(term, foreignRoom));
    }

    [Fact]
    public void Constructor_WithARoomFromTheSameStudio_IsAllowed()
    {
        var studioId = Guid.NewGuid();
        var term = ATerm();
        term.StudioId = studioId;

        var room = new Room("Studio A", 20) { StudioId = studioId };

        Assert.Equal(room.Id, AClass(term, room).RoomId);
    }

    // --- Age eligibility ---------------------------------------------------------------

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]  // inclusive lower bound
    [InlineData(10, true)]
    [InlineData(12, true)] // inclusive upper bound
    [InlineData(13, false)]
    public void AcceptsAge_BoundsAreInclusive(int age, bool expected)
    {
        Assert.Equal(expected, AClass(minimumAge: 8, maximumAge: 12).AcceptsAge(age));
    }

    [Fact]
    public void AcceptsAge_WithAnUnknownAge_Passes()
    {
        // Refusing to enrol anyone without a birthday on file turns a missing optional field
        // into a blocking error, and studios respond by typing in a fake date — which is worse
        // than knowing the age is unknown.
        Assert.True(AClass(minimumAge: 8, maximumAge: 12).AcceptsAge(null));
    }

    [Fact]
    public void AcceptsAge_WithNoBoundsAtAll_AcceptsEveryone()
    {
        Assert.True(AClass().AcceptsAge(4));
        Assert.True(AClass().AcceptsAge(70));
    }

    // --- Meeting patterns --------------------------------------------------------------

    [Fact]
    public void AddMeetingPattern_AllowsAClassToMeetTwiceAWeek()
    {
        var danceClass = AClass();

        danceClass.AddMeetingPattern(DayOfWeek.Tuesday, FourPm, AnHour);
        danceClass.AddMeetingPattern(DayOfWeek.Thursday, FourPm, AnHour);

        Assert.Equal(2, danceClass.MeetingPatterns.Count);
    }

    [Fact]
    public void AddMeetingPattern_WhenItOverlapsItsOwnClass_Throws()
    {
        // A class colliding with itself needs no other rows to detect, which makes it an
        // invariant rather than a conflict — so it belongs here and not in conflict detection.
        var danceClass = AClass();
        danceClass.AddMeetingPattern(DayOfWeek.Tuesday, FourPm, AnHour);

        Assert.Throws<InvalidOperationException>(
            () => danceClass.AddMeetingPattern(DayOfWeek.Tuesday, new TimeOnly(16, 30), AnHour));
    }

    [Fact]
    public void AClassWithNoMeetingPatterns_IsValid()
    {
        // Not a broken class — a workshop series whose dates are all entered by hand. The
        // pattern is a generator, not a requirement, so having none simply generates nothing.
        Assert.Empty(AClass().MeetingPatterns);
    }

    [Fact]
    public void MeetingPatterns_CannotBeMutatedFromOutside()
    {
        var danceClass = AClass();
        var pattern = danceClass.AddMeetingPattern(DayOfWeek.Tuesday, FourPm, AnHour);

        Assert.Throws<NotSupportedException>(
            () => ((ICollection<MeetingPattern>)danceClass.MeetingPatterns).Add(pattern));
    }

    // --- Teaching assignments ----------------------------------------------------------

    [Fact]
    public void Assign_DefaultsToTheWholeClass()
    {
        var danceClass = AClass();
        var instructor = new Member("Mei", "Chen");

        var assignment = danceClass.Assign(instructor, AssignmentRole.Lead);

        Assert.Equal(TermStart, assignment.From);
        Assert.Null(assignment.Until);
    }

    [Fact]
    public void Assign_SupportsCoTeaching()
    {
        // The first thing a single InstructorId foreign key cannot express.
        var danceClass = AClass();

        danceClass.Assign(new Member("Mei", "Chen"), AssignmentRole.Lead);
        danceClass.Assign(new Member("Luis", "Ramos"), AssignmentRole.Lead);

        Assert.Equal(2, danceClass.AssignmentsOn(TermStart).Count());
    }

    [Fact]
    public void Assign_SupportsALeadAndAnAssistantTogether()
    {
        var danceClass = AClass();
        var lead = new Member("Mei", "Chen");
        var assistant = new Member("Nadia", "Okonkwo", new DateOnly(2010, 3, 14));

        danceClass.Assign(lead, AssignmentRole.Lead);
        danceClass.Assign(assistant, AssignmentRole.Assistant);

        Assert.Equal(2, danceClass.Assignments.Count);
    }

    [Fact]
    public void Assign_SupportsASubstituteForASinglePeriod()
    {
        var danceClass = AClass();
        var substitute = new Member("Priya", "Nair");
        var oneWeek = new DateOnly(2026, 10, 13);

        var assignment = danceClass.Assign(
            substitute, AssignmentRole.Substitute, from: oneWeek, until: oneWeek);

        Assert.True(assignment.IsActiveOn(oneWeek));
        Assert.False(assignment.IsActiveOn(oneWeek.AddDays(1)));
    }

    [Fact]
    public void Assign_SupportsAnInstructorChangingMidTerm()
    {
        var danceClass = AClass();
        var leaving = new Member("Mei", "Chen");
        var arriving = new Member("Luis", "Ramos");
        var handover = new DateOnly(2026, 10, 31);

        danceClass.Assign(leaving, AssignmentRole.Lead, until: handover);
        danceClass.Assign(arriving, AssignmentRole.Lead, from: handover.AddDays(1));

        Assert.Single(danceClass.AssignmentsOn(new DateOnly(2026, 9, 15)));
        Assert.Single(danceClass.AssignmentsOn(new DateOnly(2026, 11, 15)));
        Assert.Equal(
            arriving.Id,
            danceClass.AssignmentsOn(new DateOnly(2026, 11, 15)).Single().MemberId);
    }

    [Fact]
    public void Assign_OutsideTheClassDates_Throws()
    {
        var danceClass = AClass(
            startDate: new DateOnly(2026, 10, 1),
            endDate: new DateOnly(2026, 11, 12));

        Assert.Throws<InvalidOperationException>(
            () => danceClass.Assign(new Member("Mei", "Chen"), AssignmentRole.Lead,
                from: new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public void Assign_TheSameMemberTwiceInTheSameRole_Throws()
    {
        var danceClass = AClass();
        var instructor = new Member("Mei", "Chen");

        danceClass.Assign(instructor, AssignmentRole.Lead);

        Assert.Throws<InvalidOperationException>(
            () => danceClass.Assign(instructor, AssignmentRole.Lead));
    }

    [Fact]
    public void Assign_TheSameMemberInTwoDifferentRoles_IsAllowed()
    {
        // Unusual but not wrong: someone leading for most of the term and covering as a
        // substitute for a colleague's slot is two distinct facts.
        var danceClass = AClass();
        var instructor = new Member("Mei", "Chen");

        danceClass.Assign(instructor, AssignmentRole.Lead);
        danceClass.Assign(instructor, AssignmentRole.Substitute);

        Assert.Equal(2, danceClass.Assignments.Count);
    }

    [Fact]
    public void Assign_AMemberFromAnotherStudio_Throws()
    {
        var term = ATerm();
        term.StudioId = Guid.NewGuid();

        var foreigner = new Member("Mei", "Chen") { StudioId = Guid.NewGuid() };

        Assert.Throws<InvalidOperationException>(
            () => AClass(term).Assign(foreigner, AssignmentRole.Lead));
    }

    [Fact]
    public void AnAssistantCanAlsoBeEnrolledElsewhere_TheStudentTeacherShape()
    {
        // The end-to-end shape of the case that drove the model: one Member, two studio roles,
        // teaching one class and (in a later slice) enrolled in another. Nothing duplicated.
        var term = ATerm();
        var beginnerBallet = new DanceClass(term, "Beginner Ballet", "Ballet", capacity: 12,
            minimumAge: 5, maximumAge: 7);

        var nadia = new Member("Nadia", "Okonkwo", new DateOnly(2010, 3, 14));
        nadia.GrantRole(StudioRole.Student, new DateOnly(2024, 9, 1));
        nadia.GrantRole(StudioRole.AssistantStaff, new DateOnly(2026, 9, 1));

        beginnerBallet.Assign(nadia, AssignmentRole.Assistant);

        Assert.True(nadia.IsStaffOn(TermStart));
        Assert.True(nadia.HasRole(StudioRole.Student, TermStart));
        Assert.Single(beginnerBallet.AssignmentsOn(TermStart));

        // She is sixteen, so she is far too old to be a student in the class she assists —
        // which is exactly why "has assignments" cannot be used to infer anything about
        // enrolment, and why the two live in separate entities.
        Assert.False(beginnerBallet.AcceptsAge(nadia.AgeOn(TermStart)));
    }
}
