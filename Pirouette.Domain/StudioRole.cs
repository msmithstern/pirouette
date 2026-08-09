namespace Pirouette.Domain;

/// <summary>
/// What a member is permitted to do, studio-wide. Held through <see cref="MemberRole"/>, which
/// bounds it in time.
/// </summary>
/// <remarks>
/// This answers "what may they do?" only. Two adjacent questions have their own entities and
/// must not be inferred from this one: what a member <em>teaches</em> comes from
/// <see cref="ClassAssignment"/>, and what they <em>attend</em> will come from enrolment.
/// A studio owner who teaches nothing still needs full access; a newly hired instructor is
/// staff before their first assignment exists.
///
/// <para>Persisted as a string, never as its integer value. Reordering or inserting a member
/// of this enum would otherwise silently reassign everyone's permissions — a change with no
/// compiler error, no failing test, and no visible symptom until someone sees data they
/// should not.</para>
/// </remarks>
public enum StudioRole
{
    /// <summary>Owns the studio. Full access, including billing and other users' permissions.</summary>
    Owner,

    /// <summary>Full operational access: scheduling, enrolment, rosters, contact details.</summary>
    Admin,

    /// <summary>Adult teaching or front-desk staff. Sees full rosters for their own classes.</summary>
    Staff,

    /// <summary>
    /// A helper who is usually also a student, typically a minor.
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Staff"/> because the privacy tier is a property of the human,
    /// not of the class. A 16-year-old assisting the beginner class needs the roster to take
    /// attendance and must not see classmates' home addresses, guardian contact details, or
    /// medical notes. No amount of assignment data can make that distinction — only the role
    /// can.
    /// </remarks>
    AssistantStaff,

    /// <summary>Takes classes. The majority of members, most of whom never sign in.</summary>
    Student,

    /// <summary>A parent or carer. Sees their own household's students and nothing else.</summary>
    Guardian
}
