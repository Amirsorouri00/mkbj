using AwesomeAssertions;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Services;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Services;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Pricing;

/// <summary>
/// The quoting surface the rest of the system calls. What is asserted here is mostly which
/// failures are per-variant and which are the caller's fault.
/// </summary>
public sealed class PriceQuoteServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 30, 16, 0, 0, TimeSpan.Zero);

    private readonly IVariantPriceRepository _prices = Substitute.For<IVariantPriceRepository>();
    private readonly ICurrencyRepository _currencies = Substitute.For<ICurrencyRepository>();
    private readonly IFxRateRepository _rates = Substitute.For<IFxRateRepository>();
    private readonly IFxRateOverrideRepository _overrides = Substitute.For<IFxRateOverrideRepository>();
    private readonly IPricingPolicyGateway _policy = Substitute.For<IPricingPolicyGateway>();
    private readonly IPricingChangeNotifier _notifier = Substitute.For<IPricingChangeNotifier>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PriceQuoteServiceTests()
    {
        _currencies.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            Currency.Create(840, "USD"),
            Currency.Create(978, "EUR"),
            Currency.Create(702, "SGD"),
            Currency.Create(826, "GBP", isEnabled: false),
        ]);
        _rates.GetLatestPerPairAsync(Arg.Any<CancellationToken>()).Returns([Eur("USD", 1.0812m, id: 7)]);
        _overrides.GetActiveAsync(Arg.Any<CancellationToken>()).Returns([]);
        _policy.GetFxMaxRateAgeAsync(Arg.Any<CancellationToken>()).Returns(TimeSpan.FromHours(72));
    }

    private static FxRate Eur(string quote, decimal rate, long id) =>
        FxRate.Record(id, "EUR", quote, rate, "ecb",
            DateOnly.FromDateTime(Now.UtcDateTime), Now.AddHours(-1));

    private static VariantPrice Price(long variantId, long amountMinor, string currency) =>
        VariantPrice.Create(variantId * 10, variantId, amountMinor, currency, PriceProvenance.Manual, Now);

    private FxBookLoader Books() => new(_currencies, _rates, _overrides, _policy);

    private PriceQuoteService CreateService() => new(_prices, Books(), _notifier);

    private FxSnapshotService CreateSnapshotService() => new(Books());

    private void Priced(params VariantPrice[] prices) =>
        _prices.GetByVariantIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(prices);

    [Fact]
    public async Task Quote_Omits_Unpriced_And_Unconvertible_Variants()
    {
        // 1 is priced in EUR and convertible; 2 has no price row at all; 3 is priced in GBP,
        // for which there is no rate.
        Priced(Price(1, 10000, "EUR"), Price(3, 10000, "GBP"));

        var quotes = (await CreateService().QuoteAsync([1, 2, 3], "USD", Now, Ct)).Value!;

        quotes.Keys.Should().BeEquivalentTo([1L],
            "a listing carries on without the items it cannot price, and a per-item error would "
            + "make every caller write the same loop to discard them");
    }

    [Fact]
    public async Task Quote_Disabled_Currency_Fails_Whole_Call()
    {
        Priced(Price(1, 10000, "EUR"));

        var result = await CreateService().QuoteAsync([1], "GBP", Now, Ct);

        result.Error!.Code.Should().Be("CURRENCY_NOT_ENABLED",
            "that is not a fact about any variant; it is the caller asking for a currency the "
            + "shop does not sell in");
        result.Error.HttpStatus.Should().Be(422);
    }

    [Fact]
    public async Task Quote_An_Unknown_Currency_Fails_The_Same_Way()
    {
        Priced(Price(1, 10000, "EUR"));

        (await CreateService().QuoteAsync([1], "JPY", Now, Ct)).Error!.Code
            .Should().Be("CURRENCY_NOT_ENABLED",
                "unknown and disabled both mean 'we do not sell in this'");
    }

    [Fact]
    public async Task Quote_Returns_Band_And_Snapshot()
    {
        Priced(Price(1, 10000, "EUR"));

        var quote = (await CreateService().QuoteAsync([1], "USD", Now, Ct)).Value![1];

        quote.ReferenceMinor.Should().Be(10812);
        quote.LoMinor.Should().Be(10272);
        quote.HiMinor.Should().Be(11352);
        quote.Fx!.FxRateId.Should().Be(7);
        quote.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Quote_Reads_The_Book_Once_However_Many_Variants()
    {
        Priced(Price(1, 10000, "EUR"), Price(2, 20000, "EUR"), Price(3, 30000, "EUR"));

        await CreateService().QuoteAsync([1, 2, 3], "USD", Now, Ct);

        await _rates.Received(1).GetLatestPerPairAsync(Arg.Any<CancellationToken>());
        await _overrides.Received(1).GetActiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Quote_With_No_Ids_Asks_The_Repository_Nothing()
    {
        var quotes = (await CreateService().QuoteAsync([], "USD", Now, Ct)).Value!;

        quotes.Should().BeEmpty();
        await _prices.DidNotReceiveWithAnyArgs().GetByVariantIdsAsync(default!, default);
    }

    [Fact]
    public async Task Quote_Version_Comes_From_The_Change_Notifier()
    {
        _notifier.CurrentVersionAsync(Arg.Any<CancellationToken>()).Returns(42L);

        (await CreateService().GetVersionAsync(Ct)).Should().Be(42L,
            "a second counter is how two versions drift apart");
    }

    [Fact]
    public async Task ToUsd_From_Usd_Has_No_Snapshot()
    {
        var conversion = (await CreateSnapshotService().ToUsdAsync(1250, "USD", Now, Ct)).Value!;

        conversion.AmountMinor.Should().Be(1250);
        conversion.Fx.Should().BeNull("a snapshot here would describe a rate nobody applied");
    }

    [Fact]
    public async Task ToUsd_Uses_Exact_Pair_Override()
    {
        _overrides.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(
        [
            FxRateOverride.Create(9, "SGD", "USD", 0.7550m, Guid.NewGuid(), Now.AddDays(-1), null),
        ]);

        var conversion = (await CreateSnapshotService().ToUsdAsync(1250, "SGD", Now, Ct)).Value!;

        conversion.AmountMinor.Should().Be(944, "943.75 rounds to 944");
        conversion.Fx!.FxOverrideId.Should().Be(9);
    }

    [Fact]
    public async Task ToUsd_Rejects_A_Disabled_Currency()
    {
        var result = await CreateSnapshotService().ToUsdAsync(1250, "GBP", Now, Ct);

        result.Error!.Code.Should().Be("CURRENCY_NOT_ENABLED");
    }

    [Fact]
    public async Task ToUsd_Reports_A_Missing_Rate_As_Retryable()
    {
        _rates.GetLatestPerPairAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await CreateSnapshotService().ToUsdAsync(1250, "SGD", Now, Ct);

        result.Error!.Code.Should().Be("FX_RATE_UNAVAILABLE");
        result.Error.Retryable.Should().BeTrue();
    }

    [Fact]
    public async Task ToUsd_Rounds_Half_Even_Like_A_Price_Does()
    {
        _overrides.GetActiveAsync(Arg.Any<CancellationToken>()).Returns(
        [
            FxRateOverride.Create(9, "SGD", "USD", 0.5m, Guid.NewGuid(), Now.AddDays(-1), null),
        ]);

        var service = CreateSnapshotService();

        // 125 × 0.5 = 62.5 → 62; 135 × 0.5 = 67.5 → 68. Rounded the same way the prices it is
        // compared against were, or an order could pass one check and fail the other.
        (await service.ToUsdAsync(125, "SGD", Now, Ct)).Value!.AmountMinor.Should().Be(62);
        (await service.ToUsdAsync(135, "SGD", Now, Ct)).Value!.AmountMinor.Should().Be(68);
    }
}
