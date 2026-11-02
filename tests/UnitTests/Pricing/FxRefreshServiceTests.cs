using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Jobs;
using EwaysVNext.Modules.Pricing.Adapter.Outbound.Fx;
using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Services;
using EwaysVNext.Modules.Pricing.Application.Prices.Ports.Outbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Pricing;

/// <summary>
/// Storing what a provider published. The table is append-only, so the question each test asks is
/// what a second run over the same day does.
/// </summary>
public sealed class FxRefreshServiceTests
{
    private static readonly DateOnly RateDay = new(2026, 10, 27);

    private readonly IFxRateProvider _provider = Substitute.For<IFxRateProvider>();
    private readonly IFxRateRepository _rates = Substitute.For<IFxRateRepository>();
    private readonly IPricingChangeNotifier _notifier = Substitute.For<IPricingChangeNotifier>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IIdGenerator _ids = Substitute.For<IIdGenerator>();
    private readonly FixedClock _clock = new();

    private long _nextId = 1_000;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public FxRefreshServiceTests()
    {
        _ids.Generate().Returns(_ => _nextId++);
        _provider.Name.Returns("ecb");
        _rates.GetExistingQuotesAsync("EUR", "ecb", RateDay, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal));
    }

    private FxRefreshService CreateService() =>
        new(_provider, _rates, _notifier, _unitOfWork, _ids, _clock);

    private static FxProviderBatch Batch(params (string Quote, decimal Rate)[] quotes) =>
        new("ecb", RateDay, quotes.Select(q => new FxQuote(q.Quote, q.Rate)).ToList());

    private static FxProviderBatch DailyBatch() =>
        Batch(("USD", 1.0812m), ("JPY", 163.42m), ("GBP", 0.84215m), ("SGD", 1.4321m));

    [Fact]
    public async Task Ingest_Skips_Quotes_Already_Stored_For_Rate_Date()
    {
        _rates.GetExistingQuotesAsync("EUR", "ecb", RateDay, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal) { "USD" });

        var inserted = (await CreateService().IngestAsync(DailyBatch(), Ct)).Value;

        inserted.Should().Be(3);
        await _rates.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<FxRate>>(rows => rows.All(row => row.Quote != "USD")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_Second_Run_Over_The_Same_Day_Inserts_Nothing()
    {
        _rates.GetExistingQuotesAsync("EUR", "ecb", RateDay, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal) { "USD", "JPY", "GBP", "SGD" });

        var inserted = (await CreateService().IngestAsync(DailyBatch(), Ct)).Value;

        inserted.Should().Be(0);
        await _rates.DidNotReceiveWithAnyArgs().AddRangeAsync(default!, default);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ingest_Always_Reprojects()
    {
        _rates.GetExistingQuotesAsync("EUR", "ecb", RateDay, Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal) { "USD", "JPY", "GBP", "SGD" });

        await CreateService().IngestAsync(DailyBatch(), Ct);

        await _notifier.Received(1).FxChangedAsync(Arg.Any<CancellationToken>());
        // Rates go stale because the clock moved, not because anything was written, so the
        // projection has to be rebuilt even on a run that inserted nothing.
    }

    [Fact]
    public async Task Refresh_Propagates_Provider_Failure_Without_Saving()
    {
        _provider.FetchAsync(Arg.Any<CancellationToken>()).Returns(
            Result.Failure<FxProviderBatch>(new Error("FX_PROVIDER_FAILED", "down", 502, true)));

        var result = await CreateService().RefreshAsync(Ct);

        result.Error!.Code.Should().Be("FX_PROVIDER_FAILED");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _notifier.DidNotReceiveWithAnyArgs().FxChangedAsync();
        // Not announced either: the rates we hold are still the best we have, and re-projecting
        // would move their staleness forward in the table without moving it in fact.
    }

    [Fact]
    public async Task Refresh_Ingests_What_The_Provider_Returned()
    {
        _provider.FetchAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(DailyBatch()));

        (await CreateService().RefreshAsync(Ct)).Value.Should().Be(4);
    }

    [Fact]
    public async Task A_Rate_The_Domain_Refuses_Does_Not_Cost_The_Batch()
    {
        // A zero rate and a quote equal to the base: one bad line in a published file must not
        // lose the day's other rates.
        var batch = Batch(("USD", 1.0812m), ("XXX", 0m), ("EUR", 1m), ("SGD", 1.4321m));

        var inserted = (await CreateService().IngestAsync(batch, Ct)).Value;

        inserted.Should().Be(2);
    }

    [Fact]
    public async Task Stored_Rows_Carry_The_Published_Day_And_The_Fetch_Time()
    {
        _provider.FetchAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(Batch(("USD", 1.0812m))));

        await CreateService().RefreshAsync(Ct);

        await _rates.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<FxRate>>(rows => rows.All(row =>
                row.RateDate == RateDay && row.FetchedAt == _clock.UtcNow && row.Provider == "ecb")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handler_Maps_Failure_To_RetryLater()
    {
        var refresh = Substitute.For<IFxRefreshService>();
        refresh.RefreshAsync(Arg.Any<CancellationToken>()).Returns(
            Result.Failure<int>(new Error("FX_PROVIDER_FAILED", "down", 502, true)));

        var outcome = await new FxRefreshJobHandler(refresh)
            .HandleAsync(new FxRefreshPayload(), new JobContext(1, 1, _ => Task.CompletedTask), Ct);

        outcome.Should().BeOfType<JobOutcome.RetryLaterOutcome>()
            .Which.Reason.Should().Be("FX_PROVIDER_FAILED",
                "a provider that is down is not a job that is wrong");
    }

    [Fact]
    public async Task Handler_Completes_On_Success()
    {
        var refresh = Substitute.For<IFxRefreshService>();
        refresh.RefreshAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(4));

        var outcome = await new FxRefreshJobHandler(refresh)
            .HandleAsync(new FxRefreshPayload(), new JobContext(1, 1, _ => Task.CompletedTask), Ct);

        outcome.Should().BeOfType<JobOutcome.CompletedOutcome>();
    }
}
