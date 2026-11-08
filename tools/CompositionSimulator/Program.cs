using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using EwaysVNext.Modules.Sales.Application.Composition;

namespace EwaysVNext.Tools.CompositionSimulator;

/// <summary>
/// Measures how often the composition engine can hit an exact amount, across the amount range a
/// partner may actually ask for.
/// </summary>
/// <remarks>
/// <para>
/// This file <b>defines</b> the "representative distribution" the PRD asks to document (spec
/// §10.5). The 99% target is measured against it and is not claimed beyond it: a different
/// distribution — one weighted to round hundreds, say — would give a different and probably
/// higher number, and quoting that as the engine's success rate would be dishonest.
/// </para>
/// <para>
/// It drives the real <see cref="CompositionEngine"/> over the same <see cref="SeedLikeCatalog"/>
/// the property tests use, so the simulator and the suite can never disagree about what catalogue
/// was measured. The engine is pure — no ports, no IO, no clock — so this needs no database, no
/// host and no container, and the same seed gives the same report on any machine.
/// </para>
/// <para>
/// Durations are the one thing here that is not reproducible: they are wall-clock measurements of
/// this machine. They are reported because the PRD has a latency target, and labelled as the
/// engine's own time, which excludes the quote, the database and the network.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// Amounts the PRD calls out by name. They are run in every currency on top of the random
    /// draw, because each one is a case where the engine could plausibly fail and nobody would
    /// notice in a random sample: the smallest orderable amount, a prime-ish cent value, a round
    /// number, a messy mid-range figure, and the top of the range.
    /// </summary>
    private static readonly long[] MandatedMinor =
    [
        1_000,          //        10.00
        1_743,          //        17.43
        50_000,         //       500.00
        1_284_599,      //    12,845.99
        100_000_000,    // 1,000,000.00
    ];

    private const long MinDrawMinor = 1_000;            //        10.00
    private const long MaxDrawMinor = 100_000_000;      // 1,000,000.00
    private const int MaxLines = 20;

    private static int Main(string[] args)
    {
        var samples = IntArg(args, "--samples", 2_000);
        var seed = LongArg(args, "--seed", 20_261_028);
        var deadlineMs = IntArg(args, "--deadline-ms", 8_000);
        var output = StringArg(args, "--out", "docs/reports/composition-coverage");

        Console.WriteLine(
            $"Composing {samples} random amounts plus {MandatedMinor.Length} mandated ones in each of " +
            $"{SeedLikeCatalog.Currencies.Count} currencies (seed {seed}, deadline {deadlineMs} ms).");

        var results = new List<CurrencyResult>();

        foreach (var currency in SeedLikeCatalog.Currencies)
        {
            var sw = Stopwatch.StartNew();
            results.Add(Measure(currency, samples, seed, deadlineMs));
            Console.WriteLine($"  {currency}  {sw.ElapsedMilliseconds,6} ms");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText($"{output}.json", Json(results, samples, seed, deadlineMs));
        File.WriteAllText($"{output}.md", Markdown(results, samples, seed, deadlineMs));

        Console.WriteLine($"\nWrote {output}.md and {output}.json");

        var worst = results.Min(result => result.SuccessRate);
        Console.WriteLine($"Lowest success rate: {worst:P2}");

        return 0;
    }

    private static CurrencyResult Measure(string currency, int samples, long seed, int deadlineMs)
    {
        var pool = SeedLikeCatalog.For(currency);
        var random = new Rng(unchecked((ulong)(seed ^ Hash(currency))));

        var durations = new List<double>(samples + MandatedMinor.Length);
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var deviations = new List<double>();

        // Bucketed by log decade so a report can say *where* coverage is thin rather than only
        // that it is: a 97% overall rate made entirely of failures under $20 is a different
        // product from one that misses uniformly.
        var buckets = new Dictionary<int, (int Attempts, int Successes)>();

        var successes = 0;
        var totalLines = 0L;
        var mandatedMisses = new List<long>();

        var amounts = new List<(long Minor, bool Mandated)>(samples + MandatedMinor.Length);

        foreach (var mandated in MandatedMinor)
        {
            amounts.Add((InCurrency(mandated, currency), true));
        }

        for (var i = 0; i < samples; i++)
        {
            amounts.Add((LogUniform(ref random), false));
        }

        foreach (var (amountMinor, mandated) in amounts)
        {
            var options = new CompositionOptions(MaxLines, SeedLikeCatalog.ToUsdMinor(amountMinor, currency));
            var budget = new StopwatchBudget(TimeSpan.FromMilliseconds(deadlineMs));
            var attemptSeed = unchecked((long)random.Next());

            var started = Stopwatch.GetTimestamp();
            var result = CompositionEngine.Compose(amountMinor, pool, options, attemptSeed, budget);
            durations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            var decade = Decade(amountMinor);
            var bucket = buckets.GetValueOrDefault(decade);
            buckets[decade] = (bucket.Attempts + 1, bucket.Successes + (result.IsSuccess ? 1 : 0));

            if (result.IsSuccess)
            {
                successes++;
                var lines = result.Value!.Lines;
                totalLines += lines.Count;

                // The total is exact or the engine is broken; what varies is how far each unit
                // price had to move inside its ±5% band to make the sum land. That is the number
                // a partner would notice on an invoice, so it is the one reported.
                foreach (var line in lines)
                {
                    deviations.Add(
                        Math.Abs(line.UnitPriceMinor - line.ReferenceMinor) * 10_000d / line.ReferenceMinor);
                }

                var total = lines.Sum(line => line.LineTotalMinor);
                if (total != amountMinor)
                {
                    throw new InvalidOperationException(
                        $"{currency} {amountMinor}: composed {total}, which is not the amount asked for.");
                }
            }
            else
            {
                var code = result.Error!.Code;
                failures[code] = failures.GetValueOrDefault(code) + 1;

                if (mandated)
                {
                    mandatedMisses.Add(amountMinor);
                }
            }
        }

        durations.Sort();

        return new CurrencyResult(
            currency,
            amounts.Count,
            successes,
            failures,
            Percentile(durations, 0.50),
            Percentile(durations, 0.95),
            Percentile(durations, 0.99),
            successes == 0 ? 0 : (double)totalLines / successes,
            deviations.Count == 0 ? 0 : deviations.Average(),
            mandatedMisses,
            [.. buckets
                .Where(bucket => bucket.Value.Attempts > 0)
                .Select(bucket => new BucketResult(
                    bucket.Key, bucket.Value.Attempts, bucket.Value.Successes))
                .OrderBy(bucket => bucket.Decade)]);
    }

    /// <summary>
    /// Log-uniform over $10–$1M. Uniform would put roughly 90% of the draw above $100,000 and
    /// almost nothing at the cheap end, where the ±5% band is only a few cents wide and an exact
    /// hit is hardest — which would measure the easy half of the range and call it coverage.
    /// </summary>
    private static long LogUniform(ref Rng random)
    {
        const double minLog = 3d;   // log10(1_000 minor units)
        const double maxLog = 8d;   // log10(100_000_000 minor units)

        var fraction = (random.Next() >> 11) * (1d / (1UL << 53));
        var value = (long)Math.Round(Math.Pow(10, minLog + (fraction * (maxLog - minLog))));

        return Math.Clamp(value, MinDrawMinor, MaxDrawMinor);
    }

    /// <summary>
    /// A mandated USD figure as the same real amount in another currency, so "17.43" is tested as
    /// a comparable amount everywhere rather than as 17.43 of a currency worth a third as much.
    /// </summary>
    private static long InCurrency(long usdMinor, string currency)
    {
        if (currency == "USD") return usdMinor;

        // Round-trip through the catalogue's own rates so the simulator and the pool agree.
        var probe = SeedLikeCatalog.ToUsdMinor(10_000, currency);

        return Math.Max(1, (long)Math.Round(usdMinor * 10_000d / probe));
    }

    private static int Decade(long minor) => (int)Math.Floor(Math.Log10(minor));

    private static double Percentile(List<double> sorted, double quantile)
    {
        if (sorted.Count == 0) return 0;

        var rank = quantile * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);

        return low == high ? sorted[low] : sorted[low] + ((sorted[high] - sorted[low]) * (rank - low));
    }

    private static long Hash(string value)
    {
        long hash = 17;
        foreach (var character in value) hash = unchecked((hash * 31) + character);

        return hash;
    }

    private static string Json(
        IReadOnlyList<CurrencyResult> results, int samples, long seed, int deadlineMs) =>
        JsonSerializer.Serialize(
            new
            {
                generatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                samplesPerCurrency = samples,
                seed,
                deadlineMs,
                maxLines = MaxLines,
                distribution = "log-uniform 10.00-1,000,000.00 plus the five mandated amounts",
                currencies = results.Select(result => new
                {
                    result.Currency,
                    result.Attempts,
                    result.Successes,
                    successRate = result.SuccessRate,
                    failures = result.Failures,
                    p50Ms = result.P50Ms,
                    p95Ms = result.P95Ms,
                    p99Ms = result.P99Ms,
                    meanLines = result.MeanLines,
                    meanDeviationBps = result.MeanDeviationBps,
                    mandatedMisses = result.MandatedMisses,
                    buckets = result.Buckets.Select(bucket => new
                    {
                        bucket.Decade,
                        bucket.Attempts,
                        bucket.Successes,
                        rate = bucket.Rate,
                    }),
                }),
            },
            Indented);

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static string Markdown(
        IReadOnlyList<CurrencyResult> results, int samples, long seed, int deadlineMs)
    {
        var text = new StringBuilder();

        text.AppendLine("# Composition coverage");
        text.AppendLine();
        text.AppendLine(
            CultureInfo.InvariantCulture,
            $"Generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC from " +
            $"`tools/CompositionSimulator` — {samples} random amounts plus {MandatedMinor.Length} " +
            $"mandated ones per currency, seed {seed}, deadline {deadlineMs} ms, max {MaxLines} lines.");
        text.AppendLine();
        text.AppendLine(
            "The distribution is log-uniform over 10.00–1,000,000.00 plus the mandated amounts " +
            "10.00, 17.43, 500.00, 12,845.99 and 1,000,000.00. The 99% target is measured against " +
            "this and claimed no further.");
        text.AppendLine();
        text.AppendLine("| Currency | Attempts | Success | p50 ms | p95 ms | p99 ms | Mean lines | Mean dev (bps) |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var result in results)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {result.Currency} | {result.Attempts} | {result.SuccessRate:P2} | " +
                $"{result.P50Ms:F2} | {result.P95Ms:F2} | {result.P99Ms:F2} | " +
                $"{result.MeanLines:F1} | {result.MeanDeviationBps:F0} |");
        }

        text.AppendLine();
        text.AppendLine("## Failure codes");
        text.AppendLine();

        var anyFailure = false;
        foreach (var result in results.Where(result => result.Failures.Count > 0))
        {
            anyFailure = true;
            var codes = string.Join(
                ", ",
                result.Failures.OrderByDescending(pair => pair.Value)
                    .Select(pair => $"`{pair.Key}` × {pair.Value}"));

            text.AppendLine(CultureInfo.InvariantCulture, $"- **{result.Currency}**: {codes}");
        }

        if (!anyFailure) text.AppendLine("None. Every amount composed exactly.");

        text.AppendLine();
        text.AppendLine("## Uncovered ranges");
        text.AppendLine();
        text.AppendLine("Log decades of the amount, in minor units, whose success rate is below 99%.");
        text.AppendLine();

        var thin = results
            .SelectMany(result => result.Buckets.Select(bucket => (result.Currency, bucket)))
            .Where(entry => entry.bucket.Rate < 0.99)
            .ToList();

        if (thin.Count == 0)
        {
            text.AppendLine("None — every decade is at or above 99% in every currency.");
        }
        else
        {
            text.AppendLine("| Currency | Decade (minor) | Attempts | Success |");
            text.AppendLine("|---|---|---:|---:|");

            foreach (var (currency, bucket) in thin)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| {currency} | 10^{bucket.Decade} | {bucket.Attempts} | {bucket.Rate:P2} |");
            }
        }

        text.AppendLine();
        text.AppendLine("## Mandated amounts");
        text.AppendLine();

        var missed = results.Where(result => result.MandatedMisses.Count > 0).ToList();
        if (missed.Count == 0)
        {
            text.AppendLine("All five composed exactly in all five currencies.");
        }
        else
        {
            foreach (var result in missed)
            {
                var amounts = string.Join(", ", result.MandatedMisses.Select(minor => $"{minor / 100m:N2}"));
                text.AppendLine(CultureInfo.InvariantCulture, $"- **{result.Currency}** missed: {amounts}");
            }
        }

        return text.ToString();
    }

    private static int IntArg(string[] args, string name, int fallback) =>
        int.TryParse(Raw(args, name), CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static long LongArg(string[] args, string name, long fallback) =>
        long.TryParse(Raw(args, name), CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string StringArg(string[] args, string name, string fallback) =>
        Raw(args, name) ?? fallback;

    private static string? Raw(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// A SplitMix64 of the simulator's own. Sales keeps its generator internal, and widening that
    /// so a report could borrow it would be the wrong trade: this only has to be deterministic,
    /// not identical to the engine's.
    /// </summary>
    private struct Rng(ulong seed)
    {
        private ulong _state = seed;

        public ulong Next()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                var z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;

                return z ^ (z >> 31);
            }
        }
    }

    private sealed record BucketResult(int Decade, int Attempts, int Successes)
    {
        public double Rate => Attempts == 0 ? 1 : (double)Successes / Attempts;
    }

    private sealed record CurrencyResult(
        string Currency,
        int Attempts,
        int Successes,
        Dictionary<string, int> Failures,
        double P50Ms,
        double P95Ms,
        double P99Ms,
        double MeanLines,
        double MeanDeviationBps,
        IReadOnlyList<long> MandatedMisses,
        IReadOnlyList<BucketResult> Buckets)
    {
        public double SuccessRate => Attempts == 0 ? 0 : (double)Successes / Attempts;
    }
}
