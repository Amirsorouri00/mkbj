namespace EwaysVNext.Persistence;

/// <summary>
/// Converts between a decimal amount and the integer minor units money is stored in (ADR-0017 D3).
/// <para>
/// Every currency the MVP supports has two minor units, so the factor is fixed at 100. This is the
/// one place that assumption lives; a currency with a different exponent would need a per-currency
/// factor here and a matching storage change, which is out of scope.
/// </para>
/// <para>
/// Both directions are exact and the conversion <b>refuses</b> what it cannot represent rather than
/// rounding. Silent rounding is how a composition that must total a requested amount to the cent
/// ends up a cent short.
/// </para>
/// </summary>
public static class MinorUnits
{
    private const int Factor = 100;

    public static long ToMinor(decimal amount)
    {
        if (amount != decimal.Round(amount, 2))
            throw new ArgumentException(
                $"Amount {amount} has more than two decimal places and cannot be stored exactly.",
                nameof(amount));

        return checked((long)(amount * Factor));
    }

    public static decimal FromMinor(long minor) => minor / (decimal)Factor;
}
