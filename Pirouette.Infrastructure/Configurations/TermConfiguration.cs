using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class TermConfiguration : IEntityTypeConfiguration<Term>
{
    public void Configure(EntityTypeBuilder<Term> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(t => new { t.StudioId, t.Name });
        builder.HasOne<Studio>()
            .WithMany()
            .HasForeignKey(t => t.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        // The database backstop for the guard in the Term constructor. Deliberately redundant:
        // migrations, bulk imports, and manual SQL all bypass the domain layer, and a backwards
        // term would produce nonsense rather than an error everywhere downstream.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Term_EndNotBeforeStart",
            "\"EndDate\" >= \"StartDate\""));
    }
}