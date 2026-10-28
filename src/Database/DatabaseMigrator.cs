using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EwaysVNext.Database;

/// <summary>
/// Applies the EF migrations, then every repeatable SQL script embedded in this assembly, in
/// ordinal file-name order.
/// <para>
/// The scripts exist because `init` is regenerated while the MVP is being built, and hand-written
/// SQL inside a migration would be lost on every regeneration. They must therefore be idempotent:
/// they run after every migrate, on an existing database as well as a new one. GRANTs and other
/// things EF's model cannot express live there; anything the model *can* express (extensions,
/// indexes, check constraints) stays in the model and so lands in `init`.
/// </para>
/// </summary>
public static class DatabaseMigrator
{
    private const string ScriptPrefix = "EwaysVNext.Database.Scripts.";

    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var context = services.GetRequiredService<EwaysVNextDbContext>();

        await context.Database.MigrateAsync(ct);

        var assembly = typeof(DatabaseMigrator).Assembly;
        var scriptNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ScriptPrefix, StringComparison.Ordinal)
                           && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var scriptName in scriptNames)
        {
            await using var stream = assembly.GetManifestResourceStream(scriptName)
                ?? throw new InvalidOperationException($"Embedded script '{scriptName}' could not be opened.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync(ct);

            // Each script runs through the execution strategy because retries are enabled
            // (AGENTS.md rule 8); ExecuteSqlRawAsync on its own would not be replayed.
            var strategy = context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(
                async token => await context.Database.ExecuteSqlRawAsync(sql, token), ct);
        }
    }
}
