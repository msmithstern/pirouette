using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> builder)
    {
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();

        builder.Property(h => h.Name).IsRequired().HasMaxLength(200);
        builder.Property(h => h.AddressLine1).HasMaxLength(200);
        builder.Property(h => h.City).HasMaxLength(100);
        builder.Property(h => h.Region).HasMaxLength(100);
        builder.Property(h => h.PostalCode).HasMaxLength(20);
        builder.Property(h => h.PrimaryPhone).HasMaxLength(40);
        builder.Property(h => h.PrimaryEmail).HasMaxLength(256);

        builder.HasIndex(h => new { h.StudioId, h.Name });

        builder.HasOne<Studio>()
            .WithMany()
            .HasForeignKey(h => h.StudioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
