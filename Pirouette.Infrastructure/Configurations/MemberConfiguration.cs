using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(m => m.LastName).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Email).HasMaxLength(256);
        builder.Property(m => m.Phone).HasMaxLength(40);

        // FullName and AgeOn are computed in the domain and deliberately not mapped. Storing
        // an age would be wrong within a year of being written with no event to recompute it
        // on; storing a full name would be a second source of truth for the same fact.
        builder.Ignore(m => m.FullName);

        // Roster and directory listings sort by surname then forename, and always within one
        // studio because of the tenant filter. Leading with the low-selectivity StudioId is
        // what makes the index usable for that query rather than only for lookups by name.
        builder.HasIndex(m => new { m.StudioId, m.LastName, m.FirstName });

        builder.HasOne<Studio>()
            .WithMany()
            .HasForeignKey(m => m.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        // SetNull, not Cascade. Dissolving a household must not delete the people in it — a
        // family moving to individual billing is an administrative change, not a reason for
        // three students to vanish along with their attendance history.
        builder.HasOne<Household>()
            .WithMany()
            .HasForeignKey(m => m.HouseholdId)
            .OnDelete(DeleteBehavior.SetNull);

        // Cascade here, unlike the household above: a role has no meaning without the person
        // holding it, so deleting the member should take the roles with it rather than leave
        // orphans pointing at nothing.
        builder.HasMany(m => m.Roles)
            .WithOne()
            .HasForeignKey(r => r.MemberId)
            .OnDelete(DeleteBehavior.Cascade);

        // Roles are reached through the aggregate root and written to a private backing field,
        // so EF must populate the field rather than go through the read-only property. Without
        // this, materialising a Member throws: IReadOnlyCollection has no Add.
        builder.Metadata
            .FindNavigation(nameof(Member.Roles))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
