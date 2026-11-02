using AuthorizationService.Implementation.Persistence;
using EwaysVNext.Core.Application;
using EwaysVNext.Database;
using EwaysVNext.Host.Seeding;
using EwaysVNext.Outcomes.Extensions;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Host;

/// <summary>
/// The one-shot commands the same image can run instead of serving: <c>--migrate</c>,
/// <c>--seed</c>, and the <c>--worker</c> flag that turns a serving process into a worker.
/// Production compose uses all three from a single image, so there is no second artefact to
/// build, version or keep in step.
/// </summary>
public static class HostCommands
{
    /// <summary>
    /// Flags that must be visible to service registration, applied before <c>Build()</c>.
    /// Added last so they win over appsettings and the environment — a flag the operator typed
    /// should not lose to a file.
    /// </summary>
    public static void ApplyFlags(ConfigurationManager configuration, string[] args)
    {
        if (!args.Contains("--worker")) return;

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jobs:Mode"] = "WorkerOnly",
        });
    }

    /// <summary>
    /// Runs a one-shot command. Returns the process exit code, or <c>null</c> to carry on and serve.
    /// </summary>
    public static async Task<int?> RunAsync(WebApplication app, string[] args)
    {
        if (args.Contains("--migrate")) return await MigrateAsync(app);
        if (args.Contains("--seed")) return await SeedAsync(app);

        return null;
    }

    private static async Task<int> MigrateAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        if (db.Database.IsInMemory())
            Console.WriteLine(MessageFormat.Format(ProgramMessages.MigrateSkippedFormat, db.Database.ProviderName));
        else
            await DatabaseMigrator.MigrateAsync(scope.ServiceProvider);

        var authorizationDb = scope.ServiceProvider.GetRequiredService<AuthorizationDbContext>();
        if (authorizationDb.Database.IsInMemory())
            Console.WriteLine(MessageFormat.Format(
                ProgramMessages.MigrateAuthorizationSkippedFormat, authorizationDb.Database.ProviderName));
        else
            await authorizationDb.Database.MigrateAsync();

        Console.WriteLine(ProgramMessages.MigrateDone);
        return 0;
    }

    private static async Task<int> SeedAsync(WebApplication app)
    {
        // Seed data is demo data. A partner invoice composed from fictitious SKUs is precisely
        // what the PRD forbids, so this refuses rather than trusting the operator to remember.
        // The rule lives in DemoSeedGuard so it can be tested without starting a host.
        var allowed = DemoSeedGuard.EnsureAllowed(app.Environment);
        if (allowed.IsFailure)
        {
            Console.Error.WriteLine(allowed.Error!.Message);
            return 2;
        }

        await using var scope = app.Services.CreateAsyncScope();

        // Every seeder, in order, the demo catalogue among them. A seeder has no way to return a
        // refusal, so a throw is how one reports failure — caught here and turned back into the
        // exit code the init container reads.
        try
        {
            foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>().OrderBy(s => s.Order))
                await seeder.SeedAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine(MessageFormat.Format(ProgramMessages.SeedFailedFormat, exception.Message));
            return 2;
        }

        Console.WriteLine(ProgramMessages.SeedDone);
        return 0;
    }
}
