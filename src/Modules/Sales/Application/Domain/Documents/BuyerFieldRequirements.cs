namespace EwaysVNext.Modules.Sales.Application.Domain.Documents;

/// <summary>
/// Which buyer details a document must carry before it can be issued.
/// </summary>
/// <remarks>
/// <para>
/// A requirement is a group of alternatives joined by <c>|</c>: <c>"email|phone"</c> means the
/// buyer must be reachable, by either. Every group must be satisfied, and the groups themselves
/// are configuration — what a seller's jurisdiction requires on an invoice is not something to
/// compile in.
/// </para>
/// <para>
/// The answer is the list of groups that are <b>not</b> satisfied, so the caller can tell the
/// buyer exactly what is missing rather than refusing with "details incomplete".
/// </para>
/// </remarks>
public static class BuyerFieldRequirements
{
    private const char AlternativeSeparator = '|';

    public static IReadOnlyList<string> Missing(
        IReadOnlyList<string> required, BuyerBlock buyer)
    {
        ArgumentNullException.ThrowIfNull(required);
        ArgumentNullException.ThrowIfNull(buyer);

        return [.. required.Where(group => !IsSatisfied(group, buyer))];
    }

    private static bool IsSatisfied(string group, BuyerBlock buyer) =>
        group.Split(AlternativeSeparator)
            .Any(alternative => HasValue(alternative.Trim(), buyer));

    /// <remarks>
    /// Whitespace does not count as a value. A billing address of " " satisfies a null check and
    /// prints as an empty box on the invoice.
    /// </remarks>
    private static bool HasValue(string field, BuyerBlock buyer) =>
        !string.IsNullOrWhiteSpace(Value(field, buyer));

    private static string? Value(string field, BuyerBlock buyer) => field switch
    {
        "email" => buyer.Email,
        "phone" => buyer.Phone,
        "fullName" => buyer.FullName,
        "companyName" => buyer.CompanyName,
        "billingAddress" => buyer.BillingAddress,
        "registrationNumber" => buyer.RegistrationNumber,

        // An unknown field name can never be satisfied, so a typo in configuration refuses every
        // document rather than quietly waiving a requirement.
        _ => null,
    };
}
