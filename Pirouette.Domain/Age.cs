namespace Pirouette.Domain;

/// <summary>
/// Age arithmetic, in one place.
/// </summary>
/// <remarks>
/// Extracted from <see cref="Member"/> because the read models the UI is given carry a date of
/// birth rather than an entity, and needed the same calculation. Two copies of an age
/// calculation is exactly the kind of duplication that goes unnoticed: both look obviously
/// correct, they disagree only on leap days and the day before a birthday, and the resulting
/// off-by-one in class eligibility surfaces as one confused parent per year.
/// </remarks>
public static class Age
{
    /// <summary>
    /// Completed years between <paramref name="dateOfBirth"/> and <paramref name="asOf"/>, or
    /// null if no date of birth is known.
    /// </summary>
    /// <remarks>
    /// Subtracting years and then correcting for a birthday that has not happened yet is the
    /// only formulation that handles 29 February without a special case: <c>AddYears</c> clamps
    /// it to 28 February in a non-leap year, so a leapling ages up on the 28th, consistently.
    /// </remarks>
    public static int? InCompletedYears(DateOnly? dateOfBirth, DateOnly asOf)
    {
        if (dateOfBirth is not { } dob)
        {
            return null;
        }

        var years = asOf.Year - dob.Year;

        return asOf < dob.AddYears(years) ? years - 1 : years;
    }
}
