using EwaysVNext.Modules.Sales.Application.Composition;

namespace EwaysVNext.Tools.CompositionSimulator;

/// <summary>
/// A catalogue shaped like the demo seed, built the same way every time.
/// </summary>
/// <remarks>
/// <para>
/// 400 variants over 12 categories, priced log-uniformly from $0.50 to $50,000 and converted
/// into each sellable currency. Log-uniform rather than uniform for the same reason the demo
/// seed is: a uniform draw over four orders of magnitude puts almost everything near the top and
/// leaves the cheap end — where a ±5% band is only a few cents wide and exact hits are hardest —
/// barely represented.
/// </para>
/// <para>
/// Deterministic from a fixed internal seed, so a property test that fails can be reproduced
/// from its printed inputs alone. It is shared by the unit tests and the coverage simulator by
/// linking this one file, so the two can never measure different catalogues.
/// </para>
/// </remarks>
public static class SeedLikeCatalog
{
    private const long MinUsdMinor = 50;
    private const long MaxUsdMinor = 5_000_000;
    private const int VariantCount = 400;
    private const int CategoryCount = 12;

    /// <summary>The date the demo catalogue was designed, reused so the two line up.</summary>
    private const long Seed = 20261028;

    /// <summary>Fixed-point scale for the price grid, large enough that 400 steps lose nothing.</summary>
    private const long Scale = 1_000_000_000;

    /// <summary>The 400th root of 10^5, as a fraction: 10^0.0125 ≈ 1.02920.</summary>
    private const long StepNumerator = 102_920;
    private const long StepDenominator = 100_000;

    /// <summary>Rates per 10,000, so conversion stays integer.</summary>
    private static readonly (string Code, long PerTenThousand)[] Rates =
    [
        ("USD", 10_000),
        ("EUR", 9_200),
        ("SGD", 13_500),
        ("AED", 36_725),
        ("GBP", 7_900),
    ];

    private static readonly Dictionary<string, IReadOnlyList<CompositionCandidate>> Built = [];

    public static IReadOnlyList<string> Currencies { get; } = [.. Rates.Select(rate => rate.Code)];

    public static IReadOnlyList<CompositionCandidate> For(string currency)
    {
        lock (Built)
        {
            if (Built.TryGetValue(currency, out var existing)) return existing;

            var built = Build(currency);
            Built[currency] = built;

            return built;
        }
    }

    /// <summary>An amount in some currency, back in USD minor units.</summary>
    public static long ToUsdMinor(long amountMinor, string currency) =>
        RoundHalfEven(amountMinor * 10_000, RateOf(currency));

    private static List<CompositionCandidate> Build(string currency)
    {
        var rate = RateOf(currency);
        var random = new Random64(Seed);
        var candidates = new List<CompositionCandidate>(VariantCount);

        // A five-decade grid: each step multiplies by the 400th root of 10^5, in fixed point.
        // The accumulator is scaled so truncation does not compound — stepping on the minor-unit
        // value directly loses up to a whole unit per step, which at 50 cents cancels the growth
        // entirely and produces a linear ladder from $0.50 to $4, not a five-decade spread.
        var scaled = MinUsdMinor * Scale;

        for (var i = 0; i < VariantCount; i++)
        {
            var usd = Math.Clamp(scaled / Scale, MinUsdMinor, MaxUsdMinor);

            // ±5% of jitter, so prices are not a perfectly regular ladder — a ladder would make
            // exact hits easier than any real catalogue makes them.
            var jittered = Math.Clamp(
                usd * random.Next(95, 106) / 100, MinUsdMinor, MaxUsdMinor);

            var reference = Math.Max(1, RoundHalfEven(jittered * rate, 10_000));

            candidates.Add(new CompositionCandidate(
                VariantId: i + 1,
                CategoryId: 100 + (i % CategoryCount),
                ReferenceMinor: reference,
                LoMinor: ((reference * 95) + 99) / 100,
                HiMinor: reference * 105 / 100));

            // Int128 for the intermediate: at scale 10^9 the product passes 9.2 × 10^18 around
            // step 262, and in Debug it wraps silently — the grid then stops growing and the
            // catalogue quietly spans two decades instead of five.
            scaled = (long)((Int128)scaled * StepNumerator / StepDenominator);
        }

        return candidates;
    }

    private static long RateOf(string currency) =>
        Rates.FirstOrDefault(rate => string.Equals(rate.Code, currency, StringComparison.OrdinalIgnoreCase))
            .PerTenThousand is var found && found != 0
            ? found
            : throw new ArgumentOutOfRangeException(nameof(currency), currency, "Unknown currency.");

    /// <summary>
    /// <paramref name="numerator"/> ÷ <paramref name="denominator"/>, rounding a tie to even.
    /// </summary>
    /// <remarks>
    /// The same rule <c>pricing.round_half_even</c> and <c>PriceResolver</c> use. Half away from
    /// zero would drift upward across a catalogue of conversions, and this catalogue exists to
    /// stand in for the real one.
    /// </remarks>
    private static long RoundHalfEven(long numerator, long denominator)
    {
        var quotient = numerator / denominator;
        var remainder = numerator % denominator;
        var twice = Math.Abs(remainder) * 2;

        if (twice < denominator) return quotient;
        if (twice > denominator) return quotient + Math.Sign(numerator);

        return quotient % 2 == 0 ? quotient : quotient + Math.Sign(numerator);
    }

    /// <summary>
    /// SplitMix64 again, inlined so this file depends on nothing but
    /// <see cref="CompositionCandidate"/> and can be linked into the simulator as well.
    /// </summary>
    private struct Random64(long seed)
    {
        private ulong _state = unchecked((ulong)seed);

        public int Next(int minInclusive, int maxExclusive)
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15;

                var z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
                z ^= z >> 31;

                return minInclusive + (int)(z % (ulong)(maxExclusive - minInclusive));
            }
        }
    }
}
