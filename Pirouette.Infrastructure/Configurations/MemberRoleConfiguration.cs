using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pirouette.Domain;

namespace Pirouette.Infrastructure.Configurations;

public sealed class MemberRoleConfiguration : IEntityTypeConfiguration<MemberRole>
{
    public void Configure(EntityTypeBuilder<MemberRole> builder)
    {
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        // Stored as text, not as the enum's integer value. Reordering or inserting a member of
        // StudioRole would otherwise silently reassign everyone's permissions — a change with
        // no compiler error, no failing test, and no visible symptom until someone sees data
        // they should not. It also makes the table readable in psql, which matters when the
        // question is "why can this account see that?".
        builder.Property(r => r.Role)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // Renamed at the column level only. `From` is a reserved word in SQL, and while
        // Postgres accepts it when quoted, every hand-written query and every log line would
        // then need the quotes to be exactly right. The C# names stay short because they read
        // well in domain code; the database gets names that survive being typed into psql.
        builder.Property(r => r.From).HasColumnName("EffectiveFrom");
        builder.Property(r => r.Until).HasColumnName("EffectiveUntil");

        // Answers "what may this person do right now?" — the most common authorisation query.
        builder.HasIndex(r => new { r.MemberId, r.Role });

        // Answers the reverse: "who are the current instructors?" without scanning members.
        builder.HasIndex(r => new { r.StudioId, r.Role, r.From });

        builder.HasOne<Studio>()
            .WithMany()
            .HasForeignKey(r => r.StudioId)
            .OnDelete(DeleteBehavior.Cascade);

        // The database backstop for the domain guard. Migrations, bulk imports, and manual SQL
        // all bypass the constructor, and a role ending before it begins would make every
        // as-of query silently return nothing for that person.
        // Named explicitly. EF pluralises a table from its DbSet property, and this entity
        // deliberately has none — it is reached through the member aggregate — so convention
        // would leave it as the singular "MemberRole" while every other table is plural.
        builder.ToTable("MemberRoles", t => t.HasCheckConstraint(
            "CK_MemberRole_UntilNotBeforeFrom",
            "\"EffectiveUntil\" IS NULL OR \"EffectiveUntil\" >= \"EffectiveFrom\""));
    }
}
