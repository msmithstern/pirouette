using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class ClassAssignmentConfiguration : IEntityTypeConfiguration<ClassAssignment>
{
    public void Configure(EntityTypeBuilder<ClassAssignment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // As a string, for the same reason as StudioRole: renumbering the enum must not
        // silently turn every lead instructor into an assistant.
        builder.Property(a => a.Role)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // See MemberRoleConfiguration — `From` is a reserved word in SQL.
        builder.Property(a => a.From).HasColumnName("EffectiveFrom");
        builder.Property(a => a.Until).HasColumnName("EffectiveUntil");

        // "Who teaches this class?" — the roster header.
        builder.HasIndex(a => a.DanceClassId);

        // "What does this person teach?" — the instructor's own schedule, and the row scope
        // that authorisation will be derived from once auth lands.
        builder.HasIndex(a => new { a.MemberId, a.DanceClassId });

        builder.HasOne<Studio>()
            .WithMany()
            .HasForeignKey(a => a.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not Cascade. Deleting a member who has taught classes would erase the
        // record of who taught them, and the right answer for someone who has left is to end
        // their assignment, not to remove them from history.
        builder.HasOne(a => a.Member)
            .WithMany()
            .HasForeignKey(a => a.MemberId)
            .OnDelete(DeleteBehavior.Restrict);

        // Named explicitly — see MemberRoleConfiguration. No DbSet, so no plural by convention.
        builder.ToTable("ClassAssignments", t => t.HasCheckConstraint(
            "CK_ClassAssignment_UntilNotBeforeFrom",
            "\"EffectiveUntil\" IS NULL OR \"EffectiveUntil\" >= \"EffectiveFrom\""));
    }
}
