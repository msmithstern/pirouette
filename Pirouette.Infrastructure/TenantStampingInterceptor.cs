using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pirouette.Domain;

namespace Pirouette.Infrastructure;

public sealed class TenantStampingInterceptor(ITenantProvider tenantProvider)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Assigns the current studio to any newly added tenant-owned entity that does not
    /// already have one.
    /// </summary>
    /// <remarks>
    /// Query filters are a read-time concern only — nothing stops a row being written with the
    /// wrong tenant, or none at all. This is the write-side half of that protection.
    ///
    /// <para>Only entities in <see cref="EntityState.Added"/> with an unset id are touched.
    /// Overwriting unconditionally would let a bug in seeding or admin tooling silently move a
    /// row between studios; changing tenant on an existing row is data corruption rather than
    /// a feature.</para>
    /// </remarks>
    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Materialised before mutating so the sequence is not being modified while enumerated.
        var newlyAdded = context.ChangeTracker
            .Entries<ITenantOwned>()
            .Where(e => e.State == EntityState.Added && e.Entity.StudioId == Guid.Empty)
            .ToList();

        foreach (var entry in newlyAdded)
        {
            entry.Entity.StudioId = tenantProvider.StudioId;
        }
    }
}