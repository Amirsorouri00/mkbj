using AwesomeAssertions;
using EwaysVNext.Audit;
using EwaysVNext.Audit.Implementation;
using EwaysVNext.Database;
using EwaysVNext.Domain.Common;
using EwaysVNext.Jobs;
using EwaysVNext.Jobs.Implementation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Platform;

/// <summary>
/// The hash chain is what makes tampering detectable once someone has direct database access —
/// the grant from Task 12 stops the application, not a DBA. These tests change rows as the
/// superuser and require the verifier to notice.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class AuditSealTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Seal_Chains_Events_In_Id_Order()
    {
        var clock = new MutableClock(T0);
        using var factory = await NewFactoryAsync(clock);
        var ids = await RecordAsync(factory, 3);

        clock.UtcNow = T0.AddSeconds(61);
        await SealAsync(factory);

        var seals = await SealsAsync(factory, ids);
        seals.Should().HaveCount(3);

        var rows = await RowsAsync(factory, ids);
        var previous = AuditHashing.Genesis;
        foreach (var row in rows)
        {
            previous = AuditHashing.Next(previous, row);
            seals.Single(s => s.EventId == row.Id).Hash.Should().Equal(previous);
        }
    }

    [Fact]
    public async Task Seal_Skips_Events_Younger_Than_60s()
    {
        var clock = new MutableClock(T0);
        using var factory = await NewFactoryAsync(clock);
        var ids = await RecordAsync(factory, 1);

        clock.UtcNow = T0.AddSeconds(30);
        await SealAsync(factory);

        (await SealsAsync(factory, ids)).Should().BeEmpty();
    }

    [Fact]
    public async Task Seal_Is_Idempotent()
    {
        var clock = new MutableClock(T0);
        using var factory = await NewFactoryAsync(clock);
        var ids = await RecordAsync(factory, 3);

        clock.UtcNow = T0.AddSeconds(61);
        await SealAsync(factory);
        await SealAsync(factory);

        (await SealsAsync(factory, ids)).Should().HaveCount(3);
    }

    [Fact]
    public async Task Verify_Reports_An_Intact_Chain()
    {
        var clock = new MutableClock(T0);
        using var factory = await NewFactoryAsync(clock);
        await RecordAsync(factory, 3);

        clock.UtcNow = T0.AddSeconds(61);
        await SealAsync(factory);

        var result = await VerifyAsync(factory);
        result.Intact.Should().BeTrue();
        result.SealedCount.Should().Be(3);
        result.UnsealedBelowWatermark.Should().Be(0);
    }

    [Fact]
    public async Task Verify_Detects_A_Tampered_Event()
    {
        var clock = new MutableClock(T0);
        using var factory = await NewFactoryAsync(clock);
        var ids = await RecordAsync(factory, 3);

        clock.UtcNow = T0.AddSeconds(61);
        await SealAsync(factory);

        // As the superuser, i.e. someone the application-level grant cannot stop.
        await SuperuserAsync($"UPDATE audit.audit_events SET after = '{{\"name\":\"evil\"}}' WHERE id = {ids[1]}");

        var result = await VerifyAsync(factory);
        result.Intact.Should().BeFalse();
        result.FirstMismatchEventId.Should().Be(ids[1]);
    }

    [Fact]
    public async Task Verify_Counts_A_Removed_Seal_As_A_Gap()
    {
        var clock = new MutableClock(T0);
        using var factory = await NewFactoryAsync(clock);
        var ids = await RecordAsync(factory, 3);

        clock.UtcNow = T0.AddSeconds(61);
        await SealAsync(factory);

        await SuperuserAsync($"DELETE FROM audit.audit_seals WHERE event_id = {ids[0]}");

        var result = await VerifyAsync(factory);
        result.UnsealedBelowWatermark.Should().Be(1);
        result.Intact.Should().BeFalse();
    }

    [Fact]
    public void AuditSeal_Is_Recurring_Every_Minute()
    {
        using var factory = fixture.CreateFactory();

        factory.Services.GetServices<RecurringJobRegistration>()
            .Should().ContainSingle(r => r.Type == JobTypes.AuditSeal)
            .Which.Interval.Should().Be(TimeSpan.FromMinutes(1));
    }

    private async Task<Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>> NewFactoryAsync(
        MutableClock clock)
    {
        // Cleared as the superuser: the application role cannot DELETE audit rows — that is the
        // append-only grant working, and the test must not be the one exception to it.
        await SuperuserAsync("DELETE FROM audit.audit_seals; DELETE FROM audit.audit_events;");

        return fixture.CreateFactory(configureServices: services => services.AddSingleton<IClock>(clock));
    }

    private static async Task<IReadOnlyList<long>> RecordAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        var trail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();

        for (var i = 0; i < count; i++)
            trail.Record(AuditActor.System, "test.recorded", "Test", i.ToString(), null, new { Index = i });

        await db.SaveChangesAsync();

        return await db.AuditEvents.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Id).ToListAsync();
    }

    private static async Task SealAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<AuditSealJobHandler>();
        await handler.HandleAsync(EmptyPayload.Instance, new JobContext(1, 1, _ => Task.CompletedTask));
    }

    private static async Task<AuditVerificationResult> VerifyAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAuditVerifier>().VerifyAsync();
    }

    private static async Task<List<AuditSeal>> SealsAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, IReadOnlyList<long> ids)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .AuditSeals.AsNoTracking().Where(s => ids.Contains(s.EventId)).ToListAsync();
    }

    private static async Task<List<AuditEventRow>> RowsAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, IReadOnlyList<long> ids)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>()
            .AuditEvents.AsNoTracking().Where(e => ids.Contains(e.Id)).OrderBy(e => e.Id)
            .Select(e => new AuditEventRow(
                e.Id, e.OccurredAt, e.ActorUserId, e.ActorRole, e.Action,
                e.TargetType, e.TargetId, e.Before, e.After, e.CorrelationId))
            .ToListAsync();
    }

    private async Task SuperuserAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.SuperuserConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = now;
    }
}
