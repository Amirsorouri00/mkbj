using EwaysVNext.Modules.Pricing.Application.Fx.Domain;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Pricing.Application.Fx.Services;

public sealed class FxSnapshotService(FxBookLoader books) : IFxSnapshotPort
{
    private const string Usd = "USD";

    public async Task<Result<UsdConversion>> ToUsdAsync(
        long amountMinor, string currency, DateTimeOffset now, CancellationToken ct = default)
    {
        var (book, currencies) = await books.LoadAsync(ct);

        var enabled = FxBookLoader.RequireEnabled(currencies, currency);
        if (enabled.IsFailure) return Result.Failure<UsdConversion>(enabled.Error!);

        if (string.Equals(enabled.Value, Usd, StringComparison.Ordinal))
        {
            // No conversion, so nothing to record. A snapshot here would describe a rate of 1
            // that nobody applied.
            return Result.Success(new UsdConversion(amountMinor, null));
        }

        var rate = FxRateResolver.Resolve(book, enabled.Value!, Usd, now);
        if (rate.IsFailure) return Result.Failure<UsdConversion>(rate.Error!);

        // Half-even, the same as a price conversion: this figure is compared against the
        // seller's limits, and rounding it differently from the prices it came from would let an
        // order pass one check and fail the other.
        var converted = decimal.Round(amountMinor * rate.Value!.Rate, 0, MidpointRounding.ToEven);

        return Result.Success(new UsdConversion(checked((long)converted), rate.Value.Snapshot));
    }
}
