using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EwaysVNext.Modules.Sales.Application.Money;

/// <summary>
/// What makes two partner order requests the same request.
/// </summary>
/// <remarks>
/// <para>
/// A partner whose connection drops mid-order will retry, and must not be billed twice. The
/// fingerprint is how a repeat is recognised, so it covers exactly the three things that decide
/// what is sold and for how much: the customer, the amount and the currency.
/// </para>
/// <para>
/// It deliberately covers nothing else. A retry that also corrects a typo in the buyer's phone
/// number is still the same order, and treating it as a new one would mean a second invoice for
/// a customer who asked for one basket.
/// </para>
/// </remarks>
public static class RequestFingerprint
{
    /// <summary>
    /// A lowercase hex SHA-256 over the three fields, length-prefixed.
    /// </summary>
    /// <remarks>
    /// Length-prefixed rather than simply joined with a separator, because a customer id may
    /// contain the separator: <c>("C|50000", 1)</c> and <c>("C", 50000)</c> would otherwise hash
    /// the same bytes, and one partner's retry would be answered with another's order.
    /// <para>
    /// SHA-256 rather than a cheaper hash for the same reason — a collision here is a wrong
    /// invoice, not a cache miss.
    /// </para>
    /// </remarks>
    public static string Compute(string externalCustomerId, long amountMinor, string currency)
    {
        ArgumentNullException.ThrowIfNull(externalCustomerId);
        ArgumentNullException.ThrowIfNull(currency);

        // Upper-cased, because "usd" and "USD" are the same currency and a retry that differs
        // only in letter case is the same request.
        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        var minor = amountMinor.ToString(CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        Append(builder, externalCustomerId);
        Append(builder, minor);
        Append(builder, normalizedCurrency);

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string field) =>
        builder.Append(field.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(field).Append('|');
}
