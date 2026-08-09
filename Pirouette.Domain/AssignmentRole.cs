namespace Pirouette.Domain;

/// <summary>
/// The capacity in which a member is assigned to teach a class.
/// </summary>
/// <remarks>
/// Distinct from <see cref="StudioRole"/> and not derivable from it. <see cref="StudioRole"/>
/// says what someone may do anywhere in the studio; this says what they do in one particular
/// class. The same adult instructor is <see cref="Lead"/> on Tuesday's ballet and
/// <see cref="Substitute"/> on Thursday's jazz.
///
/// <para>Persisted as a string, for the same reason as <see cref="StudioRole"/>.</para>
/// </remarks>
public enum AssignmentRole
{
    /// <summary>Runs the class and is accountable for it.</summary>
    Lead,

    /// <summary>Helps the lead. Frequently a senior student — see <see cref="StudioRole.AssistantStaff"/>.</summary>
    Assistant,

    /// <summary>Covering for someone else, usually over a short dated period.</summary>
    Substitute
}
