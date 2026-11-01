using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Brands.Domain;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Brands.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Common;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;

namespace EwaysVNext.Modules.Catalog.Application.Brands.Services;

public sealed class BrandAdminService(
    IBrandRepository repository,
    IAuditTrail audit,
    ICatalogChangeNotifier notifier,
    IUnitOfWork unitOfWork,
    IIdGenerator ids,
    IClock clock) : IBrandAdminService
{
    private const string TargetType = "Brand";

    public async Task<Result<long>> CreateAsync(string displayName, IReadOnlyList<string> aliases,
        AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var names = aliases is null ? [displayName] : aliases.Prepend(displayName);
        if (await FindClashAsync(names, excludingBrandId: null, ct) is { } clash)
            return Result.Failure<long>(NameDuplicate(clash));

        Brand brand;
        try
        {
            brand = Brand.Create(ids.Generate(), displayName, "admin", clock.UtcNow);
            ApplyAliases(brand, aliases);
        }
        catch (DomainException exception)
        {
            return Result.Failure<long>(new Error(exception.Code, exception.Message));
        }

        await repository.AddAsync(brand, ct);
        audit.Record(actor.ToAudit(), "brand.created", TargetType, Key(brand.Id),
            before: null, after: Snapshot(brand));
        await unitOfWork.SaveChangesAsync(ct);

        // After the save, never before: a notification for a write that then failed would send
        // readers looking for something that does not exist.
        await notifier.DisplayChangedAsync(ct);
        return Result.Success(brand.Id);
    }

    public async Task<Result> RenameAsync(long brandId, string displayName, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var brand = await repository.GetByIdAsync(brandId, ct);
        if (brand is null) return Result.Failure(NotFound());

        if (await FindClashAsync([displayName], brandId, ct) is { } clash)
            return Result.Failure(NameDuplicate(clash));

        var before = Snapshot(brand);
        try
        {
            brand.Rename(displayName, clock.UtcNow);
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error(exception.Code, exception.Message));
        }

        audit.Record(actor.ToAudit(), "brand.renamed", TargetType, Key(brandId), before, Snapshot(brand));
        await unitOfWork.SaveChangesAsync(ct);

        await notifier.DisplayChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetAliasesAsync(long brandId, IReadOnlyList<string> aliases,
        AdminActor actor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(aliases);

        var brand = await repository.GetByIdAsync(brandId, ct);
        if (brand is null) return Result.Failure(NotFound());

        if (await FindClashAsync(aliases, brandId, ct) is { } clash)
            return Result.Failure(NameDuplicate(clash));

        var before = Snapshot(brand);
        try
        {
            // The admin edited a whole list, so this is a replacement. Aliases not in the new
            // list go first: an alias moving between brands would otherwise collide with itself
            // on the globally unique index.
            var keep = Canonical(aliases);
            foreach (var existing in brand.Aliases.Select(alias => alias.Alias).ToArray())
            {
                if (!keep.Contains(existing))
                    brand.RemoveAlias(existing, clock.UtcNow);
            }

            ApplyAliases(brand, aliases);
        }
        catch (DomainException exception)
        {
            return Result.Failure(new Error(exception.Code, exception.Message));
        }

        audit.Record(actor.ToAudit(), "brand.aliases_changed", TargetType, Key(brandId), before, Snapshot(brand));
        await unitOfWork.SaveChangesAsync(ct);

        await notifier.DisplayChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result> SetActiveAsync(long brandId, bool active, AdminActor actor,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var brand = await repository.GetByIdAsync(brandId, ct);
        if (brand is null) return Result.Failure(NotFound());

        var before = Snapshot(brand);
        brand.SetActive(active, clock.UtcNow);

        audit.Record(actor.ToAudit(), active ? "brand.activated" : "brand.deactivated",
            TargetType, Key(brandId), before, Snapshot(brand));
        await unitOfWork.SaveChangesAsync(ct);

        // Sellability, not display: everything under a deactivated brand stops being buyable, so
        // a cart holding one of its products has to know its contents went stale.
        await notifier.SellabilityChangedAsync(ct);
        return Result.Success();
    }

    public async Task<Result<long>> EnsureAsync(string name, IReadOnlyList<string> aliases,
        string provenance, CancellationToken ct = default)
    {
        var existing = await repository.FindByNameOrAliasAsync(name, ct);
        if (existing is not null) return Result.Success(existing.Id);

        Brand brand;
        try
        {
            brand = Brand.Create(ids.Generate(), name, provenance, clock.UtcNow);
            ApplyAliases(brand, aliases);
        }
        catch (DomainException exception)
        {
            return Result.Failure<long>(new Error(exception.Code, exception.Message));
        }

        await repository.AddAsync(brand, ct);

        // Two import workers can reach this line with the same brand name at the same instant.
        // The loser reloads rather than failing: both wanted the same brand to exist, and it now
        // does. No audit entry — this records no decision anyone made.
        if (await unitOfWork.TrySaveChangesAsync(ct) == SaveOutcome.UniqueViolation)
        {
            var winner = await repository.FindByNameOrAliasAsync(name, ct);
            return winner is null
                ? Result.Failure<long>(Error.PersistenceConflict)
                : Result.Success(winner.Id);
        }

        await notifier.DisplayChangedAsync(ct);
        return Result.Success(brand.Id);
    }

    /// <summary>
    /// The first of <paramref name="names"/> already answered by a different brand, or null.
    /// <para>
    /// Checked here so the admin reads a conflict instead of a raw unique violation, and checked
    /// against aliases as well as canonical names because the index spans both: a name used as
    /// another brand's alias is already taken.
    /// </para>
    /// </summary>
    private async Task<string?> FindClashAsync(IEnumerable<string> names, long? excludingBrandId,
        CancellationToken ct)
    {
        foreach (var name in names)
        {
            if (!CanonicalKey.TryFrom(name, out _)) continue;

            var owner = await repository.FindByNameOrAliasAsync(name, ct);
            if (owner is not null && owner.Id != excludingBrandId)
                return name;
        }

        return null;
    }

    private void ApplyAliases(Brand brand, IReadOnlyList<string>? aliases)
    {
        foreach (var alias in aliases ?? [])
            brand.AddAlias(ids.Generate(), alias, clock.UtcNow);
    }

    private static HashSet<string> Canonical(IEnumerable<string> aliases)
    {
        var canonical = new HashSet<string>(StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            if (CanonicalKey.TryFrom(alias, out var key))
                canonical.Add(key);
        }

        return canonical;
    }

    private static string Key(long brandId) =>
        brandId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static object Snapshot(Brand brand) => new
    {
        brand.CanonicalName,
        brand.DisplayName,
        brand.Provenance,
        brand.IsActive,
        Aliases = brand.Aliases.Select(alias => alias.Alias).OrderBy(alias => alias, StringComparer.Ordinal).ToArray()
    };

    private static Error NotFound() => new("BRAND_NOT_FOUND", "Brand was not found.", 404);

    private static Error NameDuplicate(string name) => new(
        "BRAND_NAME_DUPLICATE",
        $"'{name}' is already used by another brand, as its name or one of its aliases.",
        409);
}
