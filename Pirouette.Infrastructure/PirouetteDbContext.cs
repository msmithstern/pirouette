using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Pirouette.Domain;

namespace Pirouette.Infrastructure;

public class PirouetteDbContext(
    DbContextOptions<PirouetteDbContext> options,
    ITenantProvider tenantProvider) : DbContext(options)
{
    /// <summary>
    /// The studio every query is scoped to. Referenced by the global query filters below.
    /// </summary>
    public Guid StudioId => tenantProvider.StudioId;

    public DbSet<Studio> Studios => Set<Studio>();
    public DbSet<Term> Terms => Set<Term>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<DanceClass> DanceClasses => Set<DanceClass>();

    // MemberRole, MeetingPattern, and ClassAssignment are reached through their aggregate roots
    // and deliberately have no DbSet. EF still maps them — they are discovered through the
    // navigations — and they still receive tenant filters and stamping, because those are
    // applied by reflection over ITenantOwned rather than over the DbSets. Omitting the
    // property is the cheapest way to say "do not query these directly"; a caller who wants
    // one goes through the class or the member that owns it, where the invariants live.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Must run first: the loop below iterates entity types, and they aren't registered
        // until their IEntityTypeConfiguration classes have been applied.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PirouetteDbContext).Assembly);

        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Adds "WHERE StudioId = @currentStudio" to every query against every entity implementing
    /// <see cref="ITenantOwned"/>.
    /// </summary>
    /// <remarks>
    /// Done by reflection rather than one explicit line per entity so that implementing the
    /// interface is sufficient on its own. A forgotten filter is silent — no error, no failing
    /// query, just rows belonging to another studio — so "remember to add a line" is not a
    /// safe design. <see cref="Studio"/> is skipped automatically because it does not implement
    /// the interface; it is the tenant rather than something owned by one.
    /// </remarks>
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var tenantOwnedTypes = modelBuilder.Model.GetEntityTypes()
            .Where(entityType => typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType));

        foreach (var entityType in tenantOwnedTypes)
        {
            // Builds the lambda `e => e.StudioId == this.StudioId` node by node. It cannot be
            // written as ordinary lambda syntax because the entity type is only known at
            // runtime, and the non-generic ModelBuilder.Entity(Type) overload takes a
            // LambdaExpression rather than a typed delegate.
            var parameter = Expression.Parameter(entityType.ClrType, "e");

            var entityStudioId = Expression.Property(parameter, nameof(ITenantOwned.StudioId));

            // Expression.Constant(this) captures the DbContext *instance*, not the current
            // Guid. This is load-bearing: EF builds the model once and caches it for the
            // lifetime of the process, so embedding a Guid literal here would freeze whichever
            // studio was active during the first request into every subsequent query. Holding
            // a reference to the context instead makes EF emit a SQL parameter that is
            // re-evaluated on each execution.
            var currentStudioId = Expression.Property(Expression.Constant(this), nameof(StudioId));

            var filter = Expression.Lambda(
                Expression.Equal(entityStudioId, currentStudioId),
                parameter);

            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }
    }
}
