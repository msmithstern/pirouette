using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class StudioConfiguration : IEntityTypeConfiguration<Studio>
{
    /// <summary>
    /// Well-known id for the studio seeded into every database.
    /// </summary>
    /// <remarks>
    /// Fixed rather than generated because configuration has to name it: every query is
    /// filtered by the current studio, so the app needs a valid id before any row exists to
    /// look one up from. A generated id would mean copying a Guid out of the database by hand
    /// after every reset, and would break a fresh clone entirely.
    ///
    /// <para>Must match <c>Pirouette:DevStudioId</c> in appsettings.Development.json.</para>
    /// </remarks>
    public static readonly Guid SeedStudioId = new("11111111-1111-1111-1111-111111111111");

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

        // Seeded through the migration rather than at startup because Studio generates its own
        // id in its constructor, so a startup seeder could not produce a predictable one
        // without either an extra constructor or reflection. Worth revisiting before this is
        // ever deployed for real — baking a fixture row into the production schema is a
        // convenience for M0, not a pattern to keep.
        builder.HasData(new
        {
            Id = SeedStudioId,
            Name = "Pirouette Dance Academy",
            TimeZoneId = "America/New_York"
        });
    }
}
