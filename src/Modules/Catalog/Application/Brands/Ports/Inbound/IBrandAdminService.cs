using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;

public interface IBrandAdminService
{
    Task<Result<long>> CreateAsync(string displayName, IReadOnlyList<string> aliases,
        AdminActor actor, CancellationToken ct = default);

    Task<Result> RenameAsync(long brandId, string displayName, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> SetAliasesAsync(long brandId, IReadOnlyList<string> aliases, AdminActor actor,
        CancellationToken ct = default);

    Task<Result> SetActiveAsync(long brandId, bool active, AdminActor actor,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a brand name to a brand id, creating the brand if nothing answers to it.
    /// <para>
    /// For publishing and seeding, not for an admin — there is no actor and no audit entry,
    /// because this records no decision. It is also the one path that races: two import workers
    /// can reach it with the same name at the same instant, so a unique violation is treated as
    /// "the other one won" and the brand is reloaded rather than reported as an error.
    /// </para>
    /// </summary>
    Task<Result<long>> EnsureAsync(string name, IReadOnlyList<string> aliases, string provenance,
        CancellationToken ct = default);
}
