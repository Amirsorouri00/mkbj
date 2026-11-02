using EwaysVNext.Outcomes;
using Microsoft.Extensions.Hosting;

namespace EwaysVNext.Host.Seeding;

/// <summary>
/// Stands between <c>--seed</c> and a production database.
/// <para>
/// The demo catalogue is fictitious — titles prefixed <c>[DEMO]</c>, in-store GTINs, placeholder
/// images — but it is 160 products a real shop would have to delete by hand, and the seeder
/// publishes them <b>active</b>. The environment check is cheap; the cleanup is not.
/// </para>
/// </summary>
public static class DemoSeedGuard
{
    public static Result EnsureAllowed(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.IsProduction()
            ? Result.Failure(new Error(
                "SEED_REFUSED_IN_PRODUCTION",
                "The demo catalogue will not be seeded into a Production environment.",
                400))
            : Result.Success();
    }
}
