using EwaysVNext.Host.Seeding;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// The demo catalogue, seeded once for the whole run.
/// </summary>
/// <remarks>
/// The partner acceptance tests compose real baskets, so they need a real catalogue — and the
/// one the <c>--seed</c> path builds is the one the product ships with. Seeding through that
/// path rather than inserting rows keeps the tests honest: if publishing breaks, these notice.
/// </remarks>
internal static class SalesTestData
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    public static async Task EnsureSeededAsync(IServiceProvider services, CancellationToken ct = default)
    {
        if (_seeded) return;

        await Gate.WaitAsync(ct);
        try
        {
            if (_seeded) return;

            using var scope = services.CreateScope();
            var result = await DemoCatalogSeeder.RunAsync(scope.ServiceProvider, ct);

            if (result.IsFailure)
                throw new InvalidOperationException($"Could not seed: {result.Error!.Message}");

            _seeded = true;
        }
        finally
        {
            Gate.Release();
        }
    }
}
