using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
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
    [Fact]
    public async Task Variant_Price_With_Overrides_Roundtrips()
    {
        using var factory = fixture.CreateFactory();
        var id = NextId();
        var variantId = NextId();
        var provenance = new PriceProvenance(
            42, "https://example/kettle", DateTimeOffset.UtcNow, 99.95m, "EUR", TaxIncluded: true);

        await InScopeAsync(factory, async db =>
        {
            var price = VariantPrice.Create(id, variantId, 9995, "EUR", provenance, DateTimeOffset.UtcNow);
            price.SetOverride(NextId(), "SGD", 14900, DateTimeOffset.UtcNow);
            price.SetOverride(NextId(), "USD", 10900, DateTimeOffset.UtcNow);
            db.VariantPrices.Add(price);
            return await db.SaveChangesAsync(Ct);
        });

        var reloaded = await InScopeAsync(factory, db =>
            db.VariantPrices.AsNoTracking()
                .Include(price => price.Overrides)
                .SingleAsync(price => price.Id == id, Ct));

        reloaded.BaseAmountMinor.Should().Be(9995);
        reloaded.BaseCurrency.Should().Be("EUR");
        // Asserted field by field, because two of them do not survive a round trip unchanged
        // and saying so is more useful than a comparison loose enough to hide it.
        reloaded.Provenance.ObservationId.Should().Be(42);
        reloaded.Provenance.SourceUrl.Should().Be("https://example/kettle");
        reloaded.Provenance.OriginalCurrency.Should().Be("EUR");
        reloaded.Provenance.TaxIncluded.Should().BeTrue();

        // Postgres timestamptz keeps microseconds; a DateTimeOffset tick is 100ns, so the last
        // digit is truncated on the way in.
        reloaded.Provenance.CollectedAt.Should().BeCloseTo(provenance.CollectedAt!.Value, TimeSpan.FromTicks(10));

        // numeric(18,4) comes back scaled to four places. The value is equal; the decimal's
        // scale is not, which is why this compares numerically rather than by record equality.
        reloaded.Provenance.OriginalAmount.Should().Be(99.95m);
        decimal.GetBits(reloaded.Provenance.OriginalAmount!.Value)[3].Should().Be(4 << 16,
            "the column's scale is what the reader gets back, not the scale it was written with");
        reloaded.Overrides.Select(@override => @override.Currency)
            .Should().BeEquivalentTo(["SGD", "USD"]);
    }

    [Fact]
    public async Task Duplicate_Override_Currency_Is_Rejected()
    {
        using var factory = fixture.CreateFactory();
        var id = NextId();

        await InScopeAsync(factory, async db =>
        {
            var price = VariantPrice.Create(id, NextId(), 9995, "EUR", PriceProvenance.Manual, DateTimeOffset.UtcNow);
            price.SetOverride(NextId(), "SGD", 14900, DateTimeOffset.UtcNow);
            db.VariantPrices.Add(price);
            return await db.SaveChangesAsync(Ct);
        });

        // Written as raw SQL, which is the only way this can happen: the aggregate upserts, so
        // a second row for one currency cannot come from a path that goes through it.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();
        var overrideId = NextId();
        var priceList = CurrencyOverride.DefaultPriceList;

        var act = async () => await db.Database.ExecuteSqlAsync($"""
            INSERT INTO pricing.currency_overrides
                (id, variant_price_id, currency, amount_minor, price_list, updated_at)
            VALUES ({overrideId}, {id}, 'SGD', 15900, {priceList}, now())
            """);

        var postgres = (await act.Should().ThrowAsync<PostgresException>()).Which;

        postgres.SqlState.Should().Be("23505");
        postgres.ConstraintName

            .Should().Be("ux_currency_overrides_price_currency_list");
    }

    [Fact]
    public async Task One_Price_Per_Variant_Is_Enforced()
    {
        using var factory = fixture.CreateFactory();
        var variantId = NextId();

        await InScopeAsync(factory, async db =>
        {
            db.VariantPrices.Add(VariantPrice.Create(
                NextId(), variantId, 1000, "EUR", PriceProvenance.Manual, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });

        var act = () => InScopeAsync(factory, async db =>
        {
            db.VariantPrices.Add(VariantPrice.Create(
                NextId(), variantId, 2000, "EUR", PriceProvenance.Manual, DateTimeOffset.UtcNow));
            return await db.SaveChangesAsync(Ct);
        });

        (await ExpectUniqueViolationAsync(act)).ConstraintName
            .Should().Be("ux_variant_prices_variant",
                "two prices would make 'what does this cost' a question with two answers");
    }

    [Theory]
    [InlineData("0.5")]
    [InlineData("1.5")]
    [InlineData("2.5")]
    [InlineData("137.5")]
    [InlineData("162.5")]
    [InlineData("2.4999")]
    [InlineData("2.5001")]
    [InlineData("10")]
    public async Task Round_Half_Even_Sql_Matches_Dotnet(string literal)
    {
        var value = decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);

        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EwaysVNextDbContext>();

        var fromSql = await db.Database
            .SqlQuery<decimal>($"SELECT pricing.round_half_even({value}) AS \"Value\"")
            .SingleAsync(Ct);

        fromSql.Should().Be(decimal.Round(value, 0, MidpointRounding.ToEven),
            "the storefront views round the same way the resolver does, or a listing page and a "
            + "quote would differ by a cent");
    }

    [Fact]
    public async Task Resolved_Rate_Projection_Replaces_All_Rows()
    {
        using var factory = fixture.CreateFactory();

        await ReplaceAsync(factory,
        [
            new ResolvedRateRow("EUR", "USD", 1.0812m, "ecb", 1, null, DateTimeOffset.UtcNow, null),
            new ResolvedRateRow("EUR", "SGD", 1.4321m, "ecb", 2, null, DateTimeOffset.UtcNow, null),
        ]);

        await ReplaceAsync(factory,
        [
            new ResolvedRateRow("EUR", "GBP", 0.85m, "ecb", 3, null, DateTimeOffset.UtcNow, null),
        ]);

        var rows = await InScopeAsync(factory, db =>
            db.Database.SqlQuery<string>($"SELECT quote AS \"Value\" FROM pricing.resolved_fx_rates")
                .ToListAsync(Ct));

        rows.Select(quote => quote.Trim()).Should().BeEquivalentTo(["GBP"],
            "a partial update would leave pairs from before an FX refresh beside pairs from after it");
    }

    [Fact]
    public async Task Rebuilding_The_Same_Pairs_Updates_Them_In_Place()
    {
        using var factory = fixture.CreateFactory();

        await ReplaceAsync(factory,
        [
            new ResolvedRateRow("EUR", "USD", 1.0812m, "ecb", 1, null, DateTimeOffset.UtcNow, null),
        ]);

        // The same pair again, which is what every refresh does. A delete-then-insert of one
        // statement would collide here: both halves see the same snapshot, so the insert would
        // hit a row the delete had not yet made invisible.
        await ReplaceAsync(factory,
        [
            new ResolvedRateRow("EUR", "USD", 1.0900m, "ecb", 2, null, DateTimeOffset.UtcNow, null),
        ]);

        var rate = await InScopeAsync(factory, db =>
            db.Database.SqlQuery<decimal>(
                    $"SELECT rate AS \"Value\" FROM pricing.resolved_fx_rates WHERE quote = 'USD'")
                .SingleAsync(Ct));

        rate.Should().Be(1.0900000000m);
    }

    private static async Task ReplaceAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory,
        IReadOnlyList<ResolvedRateRow> rows)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IResolvedRateProjection>()
            .ReplaceAllAsync(rows, Ct);
    }
}
