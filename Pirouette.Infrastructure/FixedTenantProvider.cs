namespace Pirouette.Infrastructure;

/// <summary>
/// An <see cref="ITenantProvider"/> that always reports the same studio.
/// </summary>
/// <remarks>
/// Used in two places. In the app it stands in until authentication arrives in M7, since
/// there is no signed-in user to derive a tenant from yet, and the studio comes from
/// configuration instead. In tests it is the mechanism for asking "what would a request from
/// studio A see?" — construct one per studio and give each its own context.
///
/// <para>The M7 replacement reads the studio from the current user's claims. Because callers
/// depend on <see cref="ITenantProvider"/> rather than this class, that swap is a single
/// registration change with no other code touched.</para>
/// </remarks>
public sealed class FixedTenantProvider(Guid studioId) : ITenantProvider
{
    public Guid StudioId { get; } = studioId;
}
