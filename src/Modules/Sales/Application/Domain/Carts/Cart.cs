using System.Globalization;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Sales.Application.Domain.Carts;

/// <summary>
/// One website customer's cart, holding the prices they were shown.
/// </summary>
/// <remarks>
/// <para>
/// The cart stores a <b>displayed</b> unit price, not an agreed one. It is what the customer last
/// saw, kept so that checkout can compare it against a live quote and refuse a silent change —
/// the cart never decides what anything costs, and every price reaching it came from Pricing a
/// moment earlier.
/// </para>
/// <para>
/// The quantity and line ceilings are the aggregate's own, not the database's alone: a cart of
/// ten thousand lines would compose, quote and serialize badly long before any constraint
/// complained.
/// </para>
/// </remarks>
public sealed class Cart : AggregateRoot<long>
{
    public const int MinQuantity = 1;
    public const int MaxQuantity = 999;
    public const int MaxLines = 100;

    private readonly List<CartLine> _lines = [];

    private Cart() { }

    public long CustomerId { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<CartLine> Lines => _lines;

    public long TotalMinor => _lines.Sum(line => line.LineTotalMinor);

    public static Cart Create(long id, long customerId, string currency, DateTimeOffset now) =>
        new()
        {
            Id = id,
            CustomerId = customerId,
            Currency = currency,
            UpdatedAt = now,
        };

    /// <summary>
    /// Adds a variant, or adds to the quantity already there.
    /// </summary>
    /// <remarks>
    /// Adding to an existing line refreshes its displayed price and the time it was quoted. The
    /// customer is looking at today's price for the whole line, not at a blend of two visits.
    /// </remarks>
    public void AddOrIncrement(
        long lineId, long variantId, int quantity, long displayedUnitPriceMinor, DateTimeOffset now)
    {
        AssertQuantity(quantity);

        var existing = Find(variantId);
        if (existing is not null)
        {
            AssertQuantity(checked(existing.Quantity + quantity));
            existing.Set(existing.Quantity + quantity, displayedUnitPriceMinor, now);
        }
        else
        {
            if (_lines.Count >= MaxLines)
            {
                throw new DomainException(
                    "VALIDATION_FAILED", $"A cart holds at most {Text(MaxLines)} lines.");
            }

            _lines.Add(new CartLine(lineId, variantId, quantity, displayedUnitPriceMinor, now));
        }

        UpdatedAt = now;
    }

    public void SetQuantity(
        long variantId, int quantity, long displayedUnitPriceMinor, DateTimeOffset now)
    {
        AssertQuantity(quantity);

        var line = Find(variantId)
            ?? throw new DomainException(
                "NOT_FOUND", $"Variant {Text(variantId)} is not in the cart.");

        line.Set(quantity, displayedUnitPriceMinor, now);
        UpdatedAt = now;
    }

    public void Remove(long variantId, DateTimeOffset now)
    {
        var line = Find(variantId);
        if (line is null) return;

        _lines.Remove(line);
        UpdatedAt = now;
    }

    /// <summary>
    /// Replays a fresh set of live prices onto the cart, dropping whatever is no longer quotable.
    /// </summary>
    /// <remarks>
    /// A variant missing from <paramref name="livePrices"/> is removed rather than kept at its
    /// old price. It is either unsellable or unpriced, and in both cases showing the customer a
    /// price nobody will honour is worse than showing them the line is gone.
    /// </remarks>
    public void Reprice(
        string currency, IReadOnlyDictionary<long, long> livePrices, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(livePrices);

        Currency = currency;

        foreach (var line in _lines.ToList())
        {
            if (livePrices.TryGetValue(line.VariantId, out var price))
            {
                line.Set(line.Quantity, price, now);
            }
            else
            {
                _lines.Remove(line);
            }
        }

        UpdatedAt = now;
    }

    public void Clear(DateTimeOffset now)
    {
        _lines.Clear();
        UpdatedAt = now;
    }

    private CartLine? Find(long variantId) =>
        _lines.FirstOrDefault(line => line.VariantId == variantId);

    private static void AssertQuantity(int quantity)
    {
        if (quantity is >= MinQuantity and <= MaxQuantity) return;

        throw new DomainException(
            "VALIDATION_FAILED",
            $"A quantity must be between {Text(MinQuantity)} and {Text(MaxQuantity)}.");
    }

    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
