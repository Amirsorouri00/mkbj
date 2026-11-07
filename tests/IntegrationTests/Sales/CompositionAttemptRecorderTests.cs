using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Sales.Application.Domain.Telemetry;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Sales;

/// <summary>
/// The only trace a failed composition leaves.
/// </summary>
/// <remarks>
/// A failed composition writes no order, no invoice and no payment — so without this row,
/// "the engine declines too often" would be a feeling rather than a number. It is also the one
/// write in the module that must never fail loudly: an order that succeeded must not be reported
/// as failed because its telemetry could not be stored.
/// </remarks>
[Collection(PostgresSuite.Name)]
public sealed class CompositionAttemptRecorderTests(PostgresFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Recording_Writes_One_Row()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();

        var seed = DateTimeOffset.UtcNow.Ticks;
        await scope.ServiceProvider.GetRequiredService<ICompositionAttemptRecorder>()
            .RecordAsync(Attempt(seed, succeeded: true), Ct);

        var stored = await Query(factory, seed);

        stored.Should().ContainSingle().Which.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task A_Failed_Composition_Keeps_Its_Seed_And_Error_Code()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();

        var seed = DateTimeOffset.UtcNow.Ticks + 1;
        await scope.ServiceProvider.GetRequiredService<ICompositionAttemptRecorder>()
            .RecordAsync(
                Attempt(seed, succeeded: false) with { ErrorCode = "EXACT_COMPOSITION_UNAVAILABLE" },
                Ct);

        var stored = (await Query(factory, seed)).Should().ContainSingle().Which;

        stored.ErrorCode.Should().Be("EXACT_COMPOSITION_UNAVAILABLE");

        // The seed is what makes a failure reproducible: the engine is pure, so the same seed
        // over the same pool fails the same way.
        stored.Seed.Should().Be(seed);
        stored.OrderId.Should().BeNull("a failed composition has no order");
    }

    [Fact]
    public async Task The_Caller_Context_Is_Untouched()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        await scope.ServiceProvider.GetRequiredService<ICompositionAttemptRecorder>()
            .RecordAsync(Attempt(DateTimeOffset.UtcNow.Ticks + 2, succeeded: true), Ct);

        // Its own scope, so a telemetry write can never join — and so never roll back — the save
        // that writes the order.
        db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task A_Database_Failure_Is_Logged_Rather_Than_Thrown()
    {
        using var factory = fixture.CreateFactory(new Dictionary<string, string?>
        {
            // A port nothing listens on: every connection attempt fails.
            ["ConnectionStrings:Default"] =
                "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1",
        });

        using var scope = factory.Services.CreateScope();
        var recorder = scope.ServiceProvider.GetRequiredService<ICompositionAttemptRecorder>();

        var act = async () =>
            await recorder.RecordAsync(Attempt(DateTimeOffset.UtcNow.Ticks + 3, true), Ct);

        await act.Should().NotThrowAsync(
            "an order that succeeded must not be reported as failed because its telemetry "
            + "could not be written");
    }

    private static CompositionAttemptRecord Attempt(long seed, bool succeeded) => new(
        At: DateTimeOffset.UtcNow,
        PartnerId: 7,
        Currency: "USD",
        TargetMinor: 1_284_599,
        succeeded,
        ErrorCode: null,
        DurationMs: 12,
        Lines: succeeded ? 4 : 0,
        Iterations: 3,
        CandidatePoolSize: 400,
        seed,
        StrategyVersion: "v1",
        Recompositions: 0,
        OrderId: null);

    private static async Task<List<CompositionAttempt>> Query(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, long seed)
    {
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .CompositionAttempts.AsNoTracking()
            .Where(attempt => attempt.Seed == seed)
            .ToListAsync(Ct);
    }
}
