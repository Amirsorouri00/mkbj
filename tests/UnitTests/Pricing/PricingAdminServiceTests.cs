using AwesomeAssertions;
using EwaysVNext.Audit;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Jobs;
using EwaysVNext.Modules.Pricing.Application.Currencies.Domain;
using EwaysVNext.Modules.Pricing.Application.Currencies.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Domain;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Prices.Services;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Pricing;

/// <summary>
/// Who may change what. Staff set prices, which are reversible and visible on the product they
/// belong to; only a super admin touches rates and currency enablement, because both move every
/// price at once and neither is obvious from looking at a product.
/// </summary>
public sealed class PricingAdminServiceTests
{
    private const long VariantId = 500;

    private readonly IVariantPriceRepository _prices = Substitute.For<IVariantPriceRepository>();
    private readonly ICurrencyRepository _currencies = Substitute.For<ICurrencyRepository>();
    private readonly IFxRateOverrideRepository _fxOverrides = Substitute.For<IFxRateOverrideRepository>();
    private readonly ICatalogVariantGateway _variants = Substitute.For<ICatalogVariantGateway>();
    private readonly IPricingChangeNotifier _notifier = Substitute.For<IPricingChangeNotifier>();
    private readonly IJobQueue _jobs = Substitute.For<IJobQueue>();
    private readonly IAuditTrail _audit = Substitute.For<IAuditTrail>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FixedClock _clock = new();

    private readonly AdminActor _staff = new(Guid.NewGuid(), IsSuperAdmin: false);
    private readonly AdminActor _super = new(Guid.NewGuid(), IsSuperAdmin: true);

    private long _nextId = 1_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PricingAdminServiceTests()
    {
        _ids.Generate().Returns(_ => _nextId++);
        _variants.VariantExistsAsync(VariantId, Arg.Any<CancellationToken>()).Returns(true);
        _currencies.GetByCodeAsync("EUR", Arg.Any<CancellationToken>())
            .Returns(Currency.Create(978, "EUR"));
        _currencies.GetByCodeAsync("SGD", Arg.Any<CancellationToken>())
            .Returns(Currency.Create(702, "SGD"));
        _currencies.GetByCodeAsync("GBP", Arg.Any<CancellationToken>())
            .Returns(Currency.Create(826, "GBP", isEnabled: false));
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>()).Returns(SaveOutcome.Saved);
    }

    private PricingAdminService CreateService() => new(
        _prices, _currencies, _fxOverrides, _variants, _notifier, _jobs, _audit,
        _unitOfWork, _ids, _clock);

    private VariantPrice Existing(long amountMinor = 10000, string currency = "EUR")
    {
        var price = VariantPrice.Create(1, VariantId, amountMinor, currency, PriceProvenance.Manual, _clock.UtcNow);
        _prices.GetByVariantIdAsync(VariantId, Arg.Any<CancellationToken>()).Returns(price);
        return price;
    }

    [Fact]
    public async Task Staff_Cannot_Set_Fx_Override()
    {
        var result = await CreateService().SetFxOverrideAsync("EUR", "USD", 1.2m, null, _staff, Ct);

        result.Error!.Code.Should().Be("FORBIDDEN");
        result.Error.HttpStatus.Should().Be(403);
        await _fxOverrides.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _fxOverrides.DidNotReceiveWithAnyArgs().GetForPairAsync(default!, default!, default);
    }

    [Fact]
    public async Task Staff_Cannot_Toggle_Currency()
    {
        var result = await CreateService().SetCurrencyEnabledAsync("GBP", true, _staff, Ct);

        result.Error!.Code.Should().Be("FORBIDDEN");
        await _currencies.DidNotReceiveWithAnyArgs().GetByCodeAsync(default!, default);
    }

    [Fact]
    public async Task Staff_Cannot_Request_An_Fx_Refresh()
    {
        (await CreateService().RequestFxRefreshAsync(_staff, Ct)).Error!.Code.Should().Be("FORBIDDEN");

        await _jobs.DidNotReceiveWithAnyArgs()
            .EnqueueAsync<object>(default!, default!, default, default, default);
    }

    [Fact]
    public async Task Staff_Can_Set_Price_And_Override()
    {
        var price = Existing();

        (await CreateService().SetVariantPriceAsync(VariantId, 12000, "EUR", _staff, Ct))
            .IsSuccess.Should().BeTrue();
        (await CreateService().SetCurrencyOverrideAsync(VariantId, "SGD", 15000, _staff, Ct))
            .IsSuccess.Should().BeTrue();

        price.BaseAmountMinor.Should().Be(12000);
        price.Overrides.Should().ContainSingle().Which.Currency.Should().Be("SGD");
        await _notifier.Received(2).PricesChangedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_Price_For_Unknown_Variant_Fails()
    {
        _variants.VariantExistsAsync(999, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().SetVariantPriceAsync(999, 12000, "EUR", _staff, Ct);

        result.Error!.Code.Should().Be("VARIANT_NOT_FOUND");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Base_Price_May_Use_A_Disabled_Currency()
    {
        Existing();

        var result = await CreateService().SetVariantPriceAsync(VariantId, 12000, "GBP", _staff, Ct);

        result.IsSuccess.Should().BeTrue(
            "pricing in a currency before switching it on is how a currency is made ready");
    }

    [Fact]
    public async Task Override_In_Disabled_Currency_Fails()
    {
        Existing();

        var result = await CreateService().SetCurrencyOverrideAsync(VariantId, "GBP", 9000, _staff, Ct);

        result.Error!.Code.Should().Be("CURRENCY_NOT_ENABLED",
            "an override is a price customers will be shown");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Setting_A_Price_For_An_Unknown_Currency_Fails()
    {
        _currencies.GetByCodeAsync("JPY", Arg.Any<CancellationToken>()).Returns((Currency?)null);
        Existing();

        (await CreateService().SetVariantPriceAsync(VariantId, 12000, "JPY", _staff, Ct))
            .Error!.Code.Should().Be("CURRENCY_NOT_FOUND");
    }

    [Fact]
    public async Task Fx_Override_Replace_Is_One_Save_On_The_Same_Row()
    {
        var existing = FxRateOverride.Create(
            9, "EUR", "USD", 1.2m, Guid.NewGuid(), _clock.UtcNow.AddDays(-1), null);
        existing.Deactivate(_clock.UtcNow.AddHours(-1));
        _fxOverrides.GetForPairAsync("EUR", "USD", Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateService().SetFxOverrideAsync("EUR", "USD", 1.25m, null, _super, Ct);

        result.Value.Should().Be(9, "the pair's row is unique, so there is only one to set");
        existing.Rate.Should().Be(1.25m);
        existing.IsActive.Should().BeTrue();
        await _unitOfWork.Received(1).TrySaveChangesAsync(Arg.Any<CancellationToken>());
        await _fxOverrides.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await _notifier.Received(1).FxChangedAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_First_Override_For_A_Pair_Is_Added()
    {
        _fxOverrides.GetForPairAsync("EUR", "USD", Arg.Any<CancellationToken>())
            .Returns((FxRateOverride?)null);

        var result = await CreateService().SetFxOverrideAsync("EUR", "USD", 1.25m, null, _super, Ct);

        result.IsSuccess.Should().BeTrue();
        await _fxOverrides.Received(1).AddAsync(Arg.Any<FxRateOverride>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Lost_Race_On_An_Override_Is_A_Conflict()
    {
        _fxOverrides.GetForPairAsync("EUR", "USD", Arg.Any<CancellationToken>())
            .Returns((FxRateOverride?)null);
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(SaveOutcome.UniqueViolation);

        var result = await CreateService().SetFxOverrideAsync("EUR", "USD", 1.25m, null, _super, Ct);

        result.Error!.Code.Should().Be("FX_OVERRIDE_CONFLICT");
        result.Error.HttpStatus.Should().Be(409);
        await _notifier.DidNotReceiveWithAnyArgs().FxChangedAsync();
    }

    [Fact]
    public async Task A_Stale_Replace_Is_The_Same_Conflict()
    {
        _fxOverrides.GetForPairAsync("EUR", "USD", Arg.Any<CancellationToken>())
            .Returns(FxRateOverride.Create(9, "EUR", "USD", 1.2m, Guid.NewGuid(), _clock.UtcNow, null));
        _unitOfWork.TrySaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(SaveOutcome.ConcurrencyConflict);

        (await CreateService().SetFxOverrideAsync("EUR", "USD", 1.25m, null, _super, Ct))
            .Error!.Code.Should().Be("FX_OVERRIDE_CONFLICT",
                "both mean another admin set this pair first");
    }

    [Fact]
    public async Task The_Override_Audit_Entry_Carries_Its_Whole_Before_And_After()
    {
        var existing = FxRateOverride.Create(
            9, "EUR", "USD", 1.2m, Guid.NewGuid(), _clock.UtcNow.AddDays(-1), null);
        _fxOverrides.GetForPairAsync("EUR", "USD", Arg.Any<CancellationToken>()).Returns(existing);

        await CreateService().SetFxOverrideAsync("EUR", "USD", 1.25m, null, _super, Ct);

        // The row is replaced in place, so this entry is the only record that the rate was ever
        // 1.2 — "what was the rate last week" has nowhere else to come from.
        _audit.Received(1).Record(
            Arg.Any<AuditActor>(), "fx.override.set", "FxRateOverride", "EUR/USD",
            Arg.Is<object?>(before => before != null), Arg.Any<object>());
    }

    [Fact]
    public async Task Clearing_An_Override_That_Is_Not_There_Is_Not_An_Error()
    {
        _fxOverrides.GetForPairAsync("EUR", "USD", Arg.Any<CancellationToken>())
            .Returns((FxRateOverride?)null);

        (await CreateService().ClearFxOverrideAsync("EUR", "USD", _super, Ct))
            .IsSuccess.Should().BeTrue();
        await _unitOfWork.DidNotReceiveWithAnyArgs().TrySaveChangesAsync(default);
    }

    [Fact]
    public async Task Toggling_A_Currency_Announces_An_Fx_Change_Not_A_Price_Change()
    {
        await CreateService().SetCurrencyEnabledAsync("GBP", true, _super, Ct);

        await _notifier.Received(1).FxChangedAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().PricesChangedAsync();
    }

    [Fact]
    public async Task Toggling_A_Currency_To_Its_Current_Value_Writes_Nothing()
    {
        await CreateService().SetCurrencyEnabledAsync("EUR", true, _super, Ct);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().FxChangedAsync();
    }

    [Fact]
    public async Task An_Fx_Refresh_Is_Queued_Under_One_Dedupe_Key()
    {
        await CreateService().RequestFxRefreshAsync(_super, Ct);

        await _jobs.Received(1).EnqueueAsync(
            JobTypes.FxRefresh, Arg.Any<FxRefreshRequest>(), null, "fx-refresh",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Non_Positive_Price_Is_A_Domain_Error_Not_A_Crash()
    {
        Existing();

        var result = await CreateService().SetVariantPriceAsync(VariantId, 0, "EUR", _staff, Ct);

        result.Error!.Code.Should().Be("PRICE_AMOUNT_INVALID");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Overriding_A_Variant_With_No_Price_Fails()
    {
        _prices.GetByVariantIdAsync(VariantId, Arg.Any<CancellationToken>()).Returns((VariantPrice?)null);

        (await CreateService().SetCurrencyOverrideAsync(VariantId, "SGD", 15000, _staff, Ct))
            .Error!.Code.Should().Be("PRICE_MISSING",
                "there is no base price for the override to sit beside");
    }

    [Fact]
    public async Task Publishing_Creates_Then_Updates_Same_VariantPrice()
    {
        var publishing = new PricePublishingService(
            _prices, _currencies, _variants, _notifier, _unitOfWork, _ids, _clock);
        var observed = new PriceProvenance(
            42, "https://example/kettle", _clock.UtcNow, 99.95m, "EUR", TaxIncluded: true);

        _prices.GetByVariantIdAsync(VariantId, Arg.Any<CancellationToken>()).Returns((VariantPrice?)null);
        (await publishing.SetReferencePriceAsync(VariantId, 9995, "EUR", observed, Ct))
            .IsSuccess.Should().BeTrue();

        var price = Existing();
        var later = new PriceProvenance(
            43, "https://example/kettle", _clock.UtcNow, 89.95m, "EUR", TaxIncluded: true);

        (await publishing.SetReferencePriceAsync(VariantId, 8995, "EUR", later, Ct))
            .IsSuccess.Should().BeTrue();

        price.BaseAmountMinor.Should().Be(8995);
        price.Provenance.Should().Be(later);
        await _prices.Received(1).AddAsync(Arg.Any<VariantPrice>(), Arg.Any<CancellationToken>());
    }
}
