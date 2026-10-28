using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Platform;

/// <summary>
/// What a save reports when it loses. Both outcomes exist because the write paths need to tell
/// them apart: a stale write is a retry, a duplicate key is usually a business conflict (or, for an
/// idempotent insert, a replay).
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class UnitOfWorkTests(PostgresFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Stale_Save_Returns_ConcurrencyConflict()
    {
        using var factory = fixture.CreateFactory();
        var id = NextId();
        await InsertAsync(factory, Category.Create(id, "Original", $"stale-{id}", null, null, 0, Now));

        // Scope A loads the row, scope B updates it first; A's xmin check must then fail.
        await using var scopeA = factory.Services.CreateAsyncScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        var loadedByA = await contextA.Categories.SingleAsync(c => c.Id == id);

        await using (var scopeB = factory.Services.CreateAsyncScope())
        {
            var contextB = scopeB.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
            var loadedByB = await contextB.Categories.SingleAsync(c => c.Id == id);
            loadedByB.Move(null, 7, Now);
            await contextB.SaveChangesAsync();
        }

        loadedByA.Move(null, 9, Now);
        var outcome = await scopeA.ServiceProvider.GetRequiredService<IUnitOfWork>().TrySaveChangesAsync();

        outcome.Should().Be(SaveOutcome.ConcurrencyConflict);
    }

    [Fact]
    public async Task Unique_Violation_Returns_UniqueViolation()
    {
        using var factory = fixture.CreateFactory();
        var slug = $"duplicate-{NextId()}";
        await InsertAsync(factory, Category.Create(NextId(), "First", slug, null, null, 0, Now));

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        context.Categories.Add(Category.Create(NextId(), "Second", slug, null, null, 0, Now));

        var outcome = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().TrySaveChangesAsync();

        outcome.Should().Be(SaveOutcome.UniqueViolation);
    }

    private static async Task InsertAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, Category category)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        context.Categories.Add(category);
        await context.SaveChangesAsync();
    }

    private static long NextId() => Random.Shared.NextInt64(1_000_000_000L, long.MaxValue);
}
