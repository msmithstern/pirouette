using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(r => new { r.StudioId, r.Name });
        builder.HasOne<Studio>()
            .WithMany()
            .HasForeignKey(r => r.StudioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}