namespace Pirouette.Domain;

/// <summary>
/// Marks an entity as belonging to exactly one <see cref="Studio"/>.
/// </summary>
/// <remarks>
/// Every entity implementing this is automatically given a tenant query filter and has its
/// <see cref="StudioId"/> stamped on insert. Both are applied by reflection over this
/// interface in the Infrastructure layer, so implementing it is the <em>only</em> thing
/// required to make an entity tenant-scoped — there is no list to remember to update.
///
/// <para><see cref="Studio"/> itself deliberately does not implement this. A studio is the
/// tenant rather than something owned by one, and filtering it would make resolving the
/// current tenant impossible: you would need to know the studio in order to query for it.</para>
///
/// <para>The setter exists because the insert interceptor has to assign this. Application code
/// should never set it directly.</para>
/// </remarks>
public interface ITenantOwned
{
    Guid StudioId { get; set; }
}
