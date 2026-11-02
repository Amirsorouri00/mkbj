using System.Globalization;
using System.Xml.Linq;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Inbound;
using EwaysVNext.Modules.Pricing.Application.Fx.Ports.Outbound;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Pricing.Adapter.Outbound.Fx.Ecb;

/// <summary>
/// The European Central Bank's daily reference rates.
/// <para>
/// Chosen because it is free, needs no key, and publishes against the euro — which is why the
/// euro is the hub every cross rate goes through. It covers neither the dirham (pegged, derived)
/// nor every pair directly, which is the whole reason the resolver crosses rather than looking
/// pairs up.
/// </para>
/// </summary>
public sealed class EcbFxRateProvider(HttpClient http, ILogger<EcbFxRateProvider> logger)
    : IFxRateProvider
{
    private const string DailyPath = "eurofxref-daily.xml";

    /// <summary>The ECB's own namespace; the envelope around it is gesmes, which is not read.</summary>
    private static readonly XNamespace Rates = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";

    public string Name => "ecb";

    public async Task<Result<FxProviderBatch>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(DailyPath, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    $"The rate provider answered {(int)response.StatusCode}.",
                    $"status {(int)response.StatusCode}");
            }

            await using var content = await response.Content.ReadAsStreamAsync(ct);
            return Parse(await XDocument.LoadAsync(content, LoadOptions.None, ct));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
                                              or System.Xml.XmlException)
        {
            // Network trouble and malformed XML are the same thing to a caller: the provider did
            // not give us rates this time, and trying again later is the right response to both.
            logger.LogWarning(exception, "Could not fetch rates from the ECB.");
            return Failure("The rate provider could not be reached.", exception.GetType().Name);
        }
    }

    private Result<FxProviderBatch> Parse(XDocument document)
    {
        // The day is on the middle Cube; the rates hang off it. Reading the date from the
        // document rather than using today's is the point — a published rate belongs to the day
        // it was published for, not the day we happened to ask.
        var dayElement = document.Descendants(Rates + "Cube")
            .FirstOrDefault(cube => cube.Attribute("time") is not null);

        if (dayElement is null
            || !DateOnly.TryParseExact(
                dayElement.Attribute("time")!.Value, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var rateDate))
        {
            return Failure("The rate provider's document carried no usable date.", "no date");
        }

        var quotes = new List<FxQuote>();
        foreach (var cube in dayElement.Elements(Rates + "Cube"))
        {
            var currency = cube.Attribute("currency")?.Value;
            var rate = cube.Attribute("rate")?.Value;

            // InvariantCulture, always: the document writes 1.0812 with a dot, and parsing it
            // under a comma-decimal culture would read it as ten thousand.
            if (currency is { Length: 3 }
                && rate is not null
                && decimal.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
            {
                quotes.Add(new FxQuote(currency.ToUpperInvariant(), parsed));
            }
        }

        return quotes.Count == 0
            ? Failure("The rate provider's document carried no rates.", "no rates")
            : Result.Success(new FxProviderBatch(Name, rateDate, quotes));
    }

    /// <summary>
    /// Retryable, and 502: the failure is upstream, and the next scheduled refresh will almost
    /// certainly succeed. Pricing meanwhile keeps quoting from the rates it already has until
    /// they age out.
    /// </summary>
    private static Result<FxProviderBatch> Failure(string message, string reason) =>
        Result.Failure<FxProviderBatch>(new Error(
            "FX_PROVIDER_FAILED", $"{message} ({reason})", 502, Retryable: true));
}
