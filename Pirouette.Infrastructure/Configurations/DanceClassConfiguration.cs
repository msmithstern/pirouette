using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class DanceClassConfiguration : IEntityTypeConfiguration<DanceClass>
{
    public void Configure(EntityTypeBuilder<DanceClass> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(150);
        builder.Property(c => c.Style).IsRequired().HasMaxLength(60);
        builder.Property(c => c.Level).HasMaxLength(60);

        // The schedule page lists a term's classes; the term is always the filter.
        builder.HasIndex(c => new { c.StudioId, c.TermId });

        // Supports room double-booking checks in a later slice, which look up every class in a
        // room before they can compare times.
        builder.HasIndex(c => c.RoomId);

        builder.HasOne<Studio>()
            .WithMany()
            .HasForeignKey(c => c.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade. Deleting a term that still has classes in it is almost
        // certainly a mistake, and the classes carry enrolment and attendance behind them.
        // Better to fail loudly and make the caller empty the term deliberately.
        builder.HasOne(c => c.Term)
            .WithMany()
            .HasForeignKey(c => c.TermId)
            .OnDelete(DeleteBehavior.Restrict);

        // SetNull: closing a room should leave its classes needing a new room, not delete them.
        builder.HasOne(c => c.Room)
            .WithMany()
            .HasForeignKey(c => c.RoomId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(c => c.MeetingPatterns)
            .WithOne()
            .HasForeignKey(p => p.DanceClassId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Assignments)
            .WithOne()
            .HasForeignKey(a => a.DanceClassId)
            .OnDelete(DeleteBehavior.Cascade);

        // Both collections are written through methods on the aggregate root and exposed
        // read-only, so EF has to populate the backing fields directly. See MemberConfiguration.
        builder.Metadata
            .FindNavigation(nameof(DanceClass.MeetingPatterns))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Metadata
            .FindNavigation(nameof(DanceClass.Assignments))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_DanceClass_CapacityPositive", "\"Capacity\" > 0");

            t.HasCheckConstraint(
                "CK_DanceClass_EndNotBeforeStart",
                "\"EndDate\" >= \"StartDate\"");

            // Written to tolerate NULLs on both sides rather than only one. A range with no
            // lower bound and a range with no upper bound are both legitimate, and a check
            // that forgets to allow for that rejects every adult class.
            t.HasCheckConstraint(
                "CK_DanceClass_AgeRangeOrdered",
                "\"MinimumAge\" IS NULL OR \"MaximumAge\" IS NULL OR \"MinimumAge\" <= \"MaximumAge\"");

            t.HasCheckConstraint(
                "CK_DanceClass_AgesNotNegative",
                "(\"MinimumAge\" IS NULL OR \"MinimumAge\" >= 0) AND " +
                "(\"MaximumAge\" IS NULL OR \"MaximumAge\" >= 0)");
        });
    }
}
