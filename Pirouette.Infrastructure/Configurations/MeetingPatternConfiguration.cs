using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class MeetingPatternConfiguration : IEntityTypeConfiguration<MeetingPattern>
{
    public void Configure(EntityTypeBuilder<MeetingPattern> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        // DayOfWeek stays an integer, unlike StudioRole and AssignmentRole. The BCL fixes these
        // numbers — Sunday is 0 — and cannot renumber them without breaking every .NET program
        // ever written, so the reordering risk that justifies string storage for our own enums
        // does not exist here.
        builder.Property(p => p.DayOfWeek).IsRequired();

        // TimeOnly and TimeSpan map natively to Postgres `time` and `interval` through Npgsql,
        // so neither needs a converter. EndTime is computed and deliberately not stored.
        builder.Ignore(p => p.EndTime);

        // Session generation walks a class's patterns; nothing queries patterns studio-wide.
        builder.HasIndex(p => p.DanceClassId);

        builder.HasOne<Studio>()
            .WithMany()
            .HasForeignKey(p => p.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        // Named explicitly — see MemberRoleConfiguration. No DbSet, so no plural by convention.
        builder.ToTable("MeetingPatterns", t =>
        {
            t.HasCheckConstraint("CK_MeetingPattern_DurationPositive", "\"Duration\" > INTERVAL '0'");

            t.HasCheckConstraint(
                "CK_MeetingPattern_DurationWithinLimit",
                "\"Duration\" <= INTERVAL '8 hours'");

            // Compared in seconds rather than as `"StartTime" + "Duration" <= TIME '24:00'`,
            // because adding an interval to a Postgres `time` wraps silently at midnight — so
            // the readable version of this check would be satisfied by exactly the values it
            // is meant to reject.
            t.HasCheckConstraint(
                "CK_MeetingPattern_DoesNotCrossMidnight",
                "EXTRACT(EPOCH FROM \"StartTime\") + EXTRACT(EPOCH FROM \"Duration\") <= 86400");
        });
    }
}
