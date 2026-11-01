using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Pricing;

/// <summary>
/// What the pricing tables guarantee on their own. These are the rules an FX refresh running
/// twice, or two admins setting an override at once, would otherwise get past.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class PricingPersistenceTests(PostgresFixture fixture)
{
    private static long NextId() => Random.Shared.NextInt64(300_000_000, 399_999_999);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A three-letter code that no other test is using.</summary>
    private static string NewCode()
    {
        Span<char> code = stackalloc char[3];
        for (var i = 0; i < 3; i++) code[i] = (char)('A' + Random.Shared.Next(26));
        return new string(code);
    }

    private static async Task<T> InScopeAsync<T>(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        Func<EwaysVNextDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>());
    }

    private static async Task<PostgresException> ExpectUniqueViolationAsync(Func<Task> act)
    {
        var postgres = (await act.Should().ThrowAsync<DbUpdateException>())
            .Which.InnerException.Should().BeOfType<PostgresException>().Which;

        postgres.SqlState.Should().Be("23505");
        return postgres;
    }

    [Fact]
    public async Task Five_Currencies_Are_Seeded_Enabled_With_Two_Minor_Units()
    {
        using var factory = fixture.CreateFactory();

        var seeded = await InScopeAsync(factory, db =>
            db.Currencies.AsNoTracking()
                .Where(currency => new[] { "USD", "EUR", "SGD", "AED", "GBP" }.Contains(currency.Code))
                .OrderBy(currency => currency.Code)
                .ToListAsync(Ct));

        seeded.Select(currency => currency.Code)
            .Should().Equal("AED", "EUR", "GBP", "SGD", "USD");
        seeded.Should().OnlyContain(currency => currency.IsEnabled && currency.MinorUnits == 2);

        // The ids are the ISO-4217 numeric codes, so the key carries the currency's own identity.
        seeded.Single(currency => currency.Code == "USD").Id.Should().Be(840);
        seeded.Single(currency => currency.Code == "SGD").Id.Should().Be(702);
    }

    [Fact]
    public async Task A_Duplicate_Currency_Code_Is_Rejected()
    {
        using var factory = fixture.CreateFactory();

        var act = () => InScopeAsync(factory, async db =>
        {
            db.Currencies.Add(Currency.Create(NextId(), "USD"));
            return await db.SaveChangesAsync(Ct);
        });

        (await ExpectUniqueViolationAsync(act)).ConstraintName.Should().Be("ux_currencies_code");
    }

    [Fact]
    public async Task Same_Rate_Date_From_Same_Provider_Is_Rejected()
    {
        using var factory = fixture.CreateFactory();
        var quote = NewCode();
        var day = DateOnly.FromDateTime(DateTime.UtcNow);

        await InScopeAsync(factory, async db =>
        {
            db.FxRates.Add(FxRate.Record(NextId(), "USD", quote, 1.35m, "ecb", day, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });

        var act = () => InScopeAsync(factory, async db =>
        {
            db.FxRates.Add(FxRate.Record(NextId(), "USD", quote, 1.36m, "ecb", day, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });

        (await ExpectUniqueViolationAsync(act)).ConstraintName
            .Should().Be("ux_fx_rates_pair_provider_date",
                "a refresh that runs twice must not double the table, and a provider revising a "
                + "published rate has to be a visible conflict rather than a silent second row");
    }

    [Fact]
    public async Task The_Same_Day_From_A_Different_Provider_Is_Allowed()
    {
        using var factory = fixture.CreateFactory();
        var quote = NewCode();
        var day = DateOnly.FromDateTime(DateTime.UtcNow);

        await InScopeAsync(factory, async db =>
        {
            db.FxRates.Add(FxRate.Record(NextId(), "USD", quote, 1.35m, "ecb", day, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });

        var act = () => InScopeAsync(factory, async db =>
        {
            db.FxRates.Add(FxRate.Record(NextId(), "USD", quote, 1.36m, "boe", day, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });

        await act.Should().NotThrowAsync("two providers disagreeing is a fact, not a conflict");
    }

    [Fact]
    public async Task Rate_Precision_Survives_Roundtrip()
    {
        using var factory = fixture.CreateFactory();
        var quote = NewCode();
        var id = NextId();

        await InScopeAsync(factory, async db =>
        {
            db.FxRates.Add(FxRate.Record(
                id, "USD", quote, 1.0812345678m, "ecb",
                DateOnly.FromDateTime(DateTime.UtcNow), DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });

        var reloaded = await InScopeAsync(factory, db =>
            db.FxRates.AsNoTracking().SingleAsync(rate => rate.Id == id, Ct));

        reloaded.Rate.Should().Be(1.0812345678m,
            "a rate is multiplied by an amount before anything is rounded, so precision lost "
            + "here is money lost at the other end");
    }

    [Fact]
    public async Task A_Non_Positive_Rate_Is_Rejected_By_The_Database()
    {
        using var factory = fixture.CreateFactory();
        var quote = NewCode();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        // Written past the aggregate, the way a fix-up script would.
        var act = async () => await db.Database.ExecuteSqlAsync($"""
            INSERT INTO pricing.fx_rates (id, base, quote, rate, provider, rate_date, fetched_at)
            VALUES ({NextId()}, 'USD', {quote}, 0, 'ecb', now()::date, now())
            """);

        var postgres = (await act.Should().ThrowAsync<PostgresException>()).Which;

        postgres.SqlState.Should().Be("23514");
        postgres.ConstraintName.Should().Be("ck_fx_rates_positive");
    }

    [Fact]
    public async Task Second_Active_Override_For_Pair_Is_Rejected()
    {
        using var factory = fixture.CreateFactory();
        var quote = NewCode();

        await InScopeAsync(factory, async db =>
        {
            db.FxRateOverrides.Add(FxRateOverride.Create(
                NextId(), "USD", quote, 1.40m, Guid.NewGuid(), DateTimeOffset.UtcNow, null));
            return await db.SaveChangesAsync(Ct);
        });

        var act = () => InScopeAsync(factory, async db =>
        {
            db.FxRateOverrides.Add(FxRateOverride.Create(
                NextId(), "USD", quote, 1.45m, Guid.NewGuid(), DateTimeOffset.UtcNow, null));
            return await db.SaveChangesAsync(Ct);
        });

        (await ExpectUniqueViolationAsync(act)).ConstraintName
            .Should().Be("ux_fx_rate_overrides_pair");
    }

    [Fact]
    public async Task A_Withdrawn_Override_Still_Occupies_Its_Pair()
    {
        using var factory = fixture.CreateFactory();
        var quote = NewCode();
        var id = NextId();

        await InScopeAsync(factory, async db =>
        {
            var @override = FxRateOverride.Create(
                id, "USD", quote, 1.40m, Guid.NewGuid(), DateTimeOffset.UtcNow, null);
            @override.Deactivate(DateTimeOffset.UtcNow);
            db.FxRateOverrides.Add(@override);
            return await db.SaveChangesAsync(Ct);
        });

        var act = () => InScopeAsync(factory, async db =>
        {
            db.FxRateOverrides.Add(FxRateOverride.Create(
                NextId(), "USD", quote, 1.45m, Guid.NewGuid(), DateTimeOffset.UtcNow, null));
            return await db.SaveChangesAsync(Ct);
        });

        await ExpectUniqueViolationAsync(act);

        // The index is deliberately unfiltered: a withdrawn override sitting beside a live one
        // would make "which override applies" stop having one answer. Reviving the row is the
        // only way to set this pair again.
        await InScopeAsync(factory, async db =>
        {
            var existing = await db.FxRateOverrides.SingleAsync(o => o.Id == id, Ct);
            existing.Replace(1.45m, Guid.NewGuid(), DateTimeOffset.UtcNow, null);
            return await db.SaveChangesAsync(Ct);
        });

        var revived = await InScopeAsync(factory, db =>
            db.FxRateOverrides.AsNoTracking().SingleAsync(o => o.Id == id, Ct));

        revived.IsActive.Should().BeTrue();
        revived.Rate.Should().Be(1.45m);
    }
}
