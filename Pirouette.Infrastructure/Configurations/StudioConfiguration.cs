using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class StudioConfiguration : IEntityTypeConfiguration<Studio>
{
    public void Configure(EntityTypeBuilder<Studio> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(100);

        builder.Property(s => s.TimeZoneId).IsRequired().HasMaxLength(100);

        // Indexed on Name alone, not (Id, Name). Id is the primary key and therefore already
        // unique, so a composite leading with it can never narrow anything further — the Name
        // half would cost writes and never be used. Composite indexes want their
        // low-selectivity column first, which is why (StudioId, Name) is right on Room and
        // Term but the same shape is wrong here.
        builder.HasIndex(s => s.Name);

        // Studio deliberately has no tenant query filter: it is the tenant. Filtering it would
        // make resolving the current studio impossible, since the lookup would depend on
        // already knowing the answer.
    }
}
