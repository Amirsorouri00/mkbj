using System.Globalization;
using AwesomeAssertions;
using EwaysVNext.Audit;
using EwaysVNext.Audit.Implementation;
using EwaysVNext.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Platform;

/// <summary>
/// Two guarantees: an audit row commits with the change it describes, and the application role
/// cannot rewrite history. The second is a database grant, not application code — an audit log the
/// application can edit is not evidence of anything.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class AuditTrailTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Record_Is_Persisted_Only_By_The_Callers_Save()
    {
        using var factory = fixture.CreateFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        var trail = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
        var targetId = Random.Shared.NextInt64(1, long.MaxValue).ToString(CultureInfo.InvariantCulture);

        trail.Record(AuditActor.System, "category.renamed", "Category", targetId,
            new { Name = "before" }, new { Name = "after" });

        (await Count(db, targetId)).Should().Be(0, "Record only stages the entry");

        await db.SaveChangesAsync();

        var row = await db.AuditEvents.AsNoTracking().SingleAsync(e => e.TargetId == targetId);
        row.Action.Should().Be("category.renamed");
        row.TargetType.Should().Be("Category");
        row.ActorRole.Should().Be("system");
        row.Before.Should().Contain("before");
        row.After.Should().Contain("after");
    }

    [Theory]
    [InlineData("UPDATE audit.audit_events SET action = 'x'")]
    [InlineData("DELETE FROM audit.audit_events")]
    [InlineData("UPDATE audit.audit_seals SET hash = '\\x00'")]
    [InlineData("DELETE FROM audit.audit_seals")]
    public async Task App_Role_Cannot_Rewrite_Audit_History(string sql)
    {
        var act = async () => await ExecuteAsync(fixture.AppConnectionString, sql);

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("42501", "insufficient privilege");
    }

    [Fact]
    public async Task App_Role_Can_Insert_Audit_Events()
    {
        using var factory = fixture.CreateFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Random.Shared.NextInt64(1, long.MaxValue),
            OccurredAt = DateTimeOffset.UtcNow,
            ActorUserId = Guid.NewGuid(),
            ActorRole = "staff",
            Action = "test.inserted",
            TargetType = "Test",
            TargetId = "1",
            CorrelationId = "test",
        });

        var act = async () => await db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    private static Task<int> Count(EwaysVNextDbContext db, string targetId) =>
        db.AuditEvents.AsNoTracking().CountAsync(e => e.TargetId == targetId);

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
