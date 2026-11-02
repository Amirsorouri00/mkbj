using EwaysVNext.Core.Application;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Host.Seeding;

/// <summary>
/// The demo catalogue as an ordinary <see cref="IDataSeeder"/>.
/// </summary>
/// <remarks>
/// <para>
/// It runs last — the reference data every other seeder creates (enabled currencies, the
/// permission matrix, the seller profile) is what the catalogue is published on top of.
/// </para>
/// <para>
/// A failure throws rather than returning, because <see cref="IDataSeeder"/> has no channel for
/// a refusal: the host's <c>--seed</c> turns it back into an exit code. Half a seeded catalogue
/// reported as success is the outcome worth avoiding.
/// </para>
/// </remarks>
public sealed class DemoCatalogDataSeeder(IServiceProvider services, ILogger<DemoCatalogDataSeeder> logger)
    : IDataSeeder
{
    public int Order => 1000;

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var result = await DemoCatalogSeeder.RunAsync(services, ct);

        if (result.IsFailure)
            throw new InvalidOperationException($"{result.Error!.Code}: {result.Error.Message}");

        var summary = result.Value!;
        logger.LogInformation(
            "Seeded {Categories} categories, {Products} products, {Variants} variants, {Prices} prices.",
            summary.Categories, summary.Products, summary.Variants, summary.Prices);
    }
}
