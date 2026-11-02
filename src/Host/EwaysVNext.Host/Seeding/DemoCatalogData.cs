using System.Globalization;

namespace EwaysVNext.Host.Seeding;

public sealed record DemoCategory(string Root, string Child);

public sealed record DemoVariant(
    IReadOnlyDictionary<string, string> Attributes,
    long BaseAmountMinor,
    string BaseCurrency);

public sealed record DemoProduct(
    string Title,
    string Brand,
    DemoCategory Category,
    string Gtin,
    byte Red,
    byte Green,
    byte Blue,
    IReadOnlyList<DemoVariant> Variants);

/// <summary>
/// A fictitious catalogue, generated once and identically on every run.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic from a fixed seed, so a demo shows the same products to every person who runs
/// it and a failing test can be reproduced from the same data. "Random" demo data that differs
/// per run turns a reproducible bug into a story about what someone saw once.
/// </para>
/// <para>
/// Everything here is marked as fictitious on purpose: titles carry <c>[DEMO]</c>, brands are
/// <c>Demo …</c>, and the GTINs sit in GS1's <c>20…</c> in-store range, which no manufacturer
/// may use. Demo data that could be mistaken for a real product is demo data that ends up in
/// someone's price comparison.
/// </para>
/// </remarks>
public static class DemoCatalogData
{
    /// <summary>The date this catalogue was designed, used as the seed so the data never moves.</summary>
    private const int Seed = 20261028;

    public const string TitlePrefix = "[DEMO] ";

    public const string ShortDescription = "Fictitious demo product — not for sale.";

    /// <summary>$0.50 — the smallest price the composition engine has to cope with.</summary>
    public const long MinPriceMinor = 50;

    /// <summary>$50,000 — the largest. Together they bracket four orders of magnitude.</summary>
    public const long MaxPriceMinor = 5_000_000;

    private static readonly string[] RootNames =
    [
        "Kitchen", "Office", "Garden", "Audio", "Lighting", "Tools",
        "Sports", "Toys", "Bags", "Bath", "Pet", "Stationery",
    ];

    private static readonly string[] ChildNames =
    [
        "Kettles", "Desks", "Planters", "Headphones", "Lamps", "Drills",
        "Rackets", "Puzzles", "Backpacks", "Towels", "Bowls", "Notebooks",
    ];

    private static readonly string[] BrandWords =
    [
        "Northwind", "Harbour", "Meridian", "Lumen", "Keystone", "Alloy", "Cobalt",
        "Quarry", "Orchard", "Summit", "Beacon", "Fathom", "Trellis", "Anvil",
        "Pike", "Willow", "Copper", "Verge", "Haven", "Tundra",
    ];

    private static readonly string[] Sizes = ["S", "M", "L"];

    private static readonly string[] Colours = ["Red", "Blue", "Green"];

    private static readonly Lazy<IReadOnlyList<DemoProduct>> LazyProducts = new(Build);

    public static IReadOnlyList<DemoCategory> Categories { get; } =
        RootNames.Select((root, index) => new DemoCategory(root, ChildNames[index])).ToList();

    public static IReadOnlyList<DemoProduct> Products => LazyProducts.Value;

    private static List<DemoProduct> Build()
    {
        var random = new Random(Seed);
        var products = new List<DemoProduct>();
        var gtinSequence = 1L;

        for (var categoryIndex = 0; categoryIndex < Categories.Count; categoryIndex++)
        {
            var category = Categories[categoryIndex];

            // Ten multi-variant products, three single-variant ones, and one more single in the
            // first four categories — 400 variants in total, which is enough for paging and
            // composition to be exercised without the seed taking a minute to run.
            var singles = 3 + (categoryIndex < 4 ? 1 : 0);

            for (var n = 0; n < 10; n++)
            {
                products.Add(Product(random, category, categoryIndex, n, ref gtinSequence, variantCount: 3));
            }

            for (var n = 0; n < singles; n++)
            {
                products.Add(Product(random, category, categoryIndex, 10 + n, ref gtinSequence, variantCount: 1));
            }
        }

        PinPriceExtremes(products);
        return products;
    }

    private static DemoProduct Product(
        Random random, DemoCategory category, int categoryIndex, int ordinal,
        ref long gtinSequence, int variantCount)
    {
        var brand = $"Demo {BrandWords[random.Next(BrandWords.Length)]}";
        var currency = CurrencyFor(ordinal);

        // Sizes or colours, never both: a demo variant axis exists to show that variants work,
        // and two axes would make the signature harder to read than the feature it demonstrates.
        var useSize = random.Next(2) == 0;

        var variants = new List<DemoVariant>(variantCount);
        for (var index = 0; index < variantCount; index++)
        {
            // A single-variant product has an EMPTY signature, which is the catalogue's rule for
            // "this product has only one form" — worth having in the demo data, since it is the
            // shape most products actually take.
            var attributes = variantCount == 1
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>
                {
                    [useSize ? "size" : "colour"] = useSize ? Sizes[index] : Colours[index],
                };

            variants.Add(new DemoVariant(attributes, LogUniformPrice(random), currency));
        }

        var gtin = Gtin(gtinSequence++);

        return new DemoProduct(
            $"{TitlePrefix}{category.Child[..^1]} {ordinal + 1} ({category.Root})",
            brand,
            category,
            gtin,
            Red: (byte)(40 + (categoryIndex * 17 % 180)),
            Green: (byte)(60 + (ordinal * 13 % 180)),
            Blue: (byte)(80 + ((categoryIndex + ordinal) * 7 % 160)),
            variants);
    }

    /// <summary>
    /// Base currency by position: every 5th product euros, every 7th Singapore dollars, every
    /// 11th sterling, everything else US dollars. Precedence in that order, so the mix is fixed
    /// rather than depending on how the conditions happen to be written.
    /// </summary>
    private static string CurrencyFor(int ordinal) =>
        (ordinal + 1) % 5 == 0 ? "EUR"
        : (ordinal + 1) % 7 == 0 ? "SGD"
        : (ordinal + 1) % 11 == 0 ? "GBP"
        : "USD";

    /// <summary>
    /// Log-uniform, not uniform: a uniform draw over four orders of magnitude puts almost every
    /// product near the top and leaves the cheap end — where rounding and the price band are
    /// most fragile — barely represented.
    /// </summary>
    private static long LogUniformPrice(Random random)
    {
        var low = Math.Log(MinPriceMinor);
        var high = Math.Log(MaxPriceMinor);
        var drawn = Math.Exp(low + (random.NextDouble() * (high - low)));

        return Math.Clamp((long)Math.Round(drawn), MinPriceMinor, MaxPriceMinor);
    }

    /// <summary>
    /// Pins the two extremes onto real variants, so the catalogue always contains both ends of
    /// the supported range whatever the draw produced.
    /// </summary>
    private static void PinPriceExtremes(List<DemoProduct> products)
    {
        Pin(products, "Kitchen", MinPriceMinor);
        Pin(products, "Audio", MaxPriceMinor);
    }

    private static void Pin(List<DemoProduct> products, string root, long amountMinor)
    {
        var index = products.FindIndex(product =>
            string.Equals(product.Category.Root, root, StringComparison.Ordinal));

        var product = products[index];
        var variants = product.Variants.ToList();
        variants[0] = variants[0] with { BaseAmountMinor = amountMinor, BaseCurrency = "USD" };
        products[index] = product with { Variants = variants };
    }

    /// <summary>
    /// A GTIN-13 in GS1's <c>20</c> prefix — the restricted in-store range, which no
    /// manufacturer may be assigned. The check digit is real, so the identifier passes the
    /// catalogue's own validation and exercises it.
    /// </summary>
    private static string Gtin(long sequence)
    {
        var payload = "20" + sequence.ToString("D10", CultureInfo.InvariantCulture);

        var sum = 0;
        var weight = 3;
        for (var index = payload.Length - 1; index >= 0; index--)
        {
            sum += (payload[index] - '0') * weight;
            weight = weight == 3 ? 1 : 3;
        }

        return payload + ((10 - (sum % 10)) % 10).ToString(CultureInfo.InvariantCulture);
    }
}
