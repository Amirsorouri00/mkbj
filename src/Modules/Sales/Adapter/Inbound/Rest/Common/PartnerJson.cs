using System.Security.Claims;
using EwaysVNext.Core.Application.Security;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Common;

/// <summary>
/// The partner API's wire format: snake_case, and only here.
/// </summary>
/// <remarks>
/// <para>
/// PRD §9 specifies snake_case for the partner surface. The shop and admin surfaces stay
/// camelCase, so this is a deliberate island rather than a global setting — a partner
/// integration written against the published contract must not break because the storefront
/// changed its mind about casing.
/// </para>
/// <para>
/// Binding and writing share these options. If only responses used them, a partner would be
/// told to send <c>external_order_id</c> and then find that only <c>externalOrderId</c> bound.
/// </para>
/// </remarks>
public static class PartnerJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper) },
    };
}

/// <summary>What the request itself says about its caller.</summary>
public static class CallerClaims
{
    /// <summary>
    /// The correlation id for this request.
    /// </summary>
    /// <remarks>
    /// The correlation middleware sets <c>TraceIdentifier</c> from the <c>X-Correlation-Id</c>
    /// header, so this is the same value the response carries and the order stores — one thread
    /// from a partner's support ticket to a log line.
    /// </remarks>
    /// <summary>
    /// The shop customer behind a request, or null if this is not one.
    /// </summary>
    /// <remarks>
    /// The reading is <see cref="ShopIdentityClaims"/>'s, so the REST and GraphQL surfaces
    /// cannot disagree about who is shopping; this only maps the result onto the port's own
    /// type, which Application owns and knows nothing about claims.
    /// </remarks>
    public static ShopCaller? ShopCallerOf(ClaimsPrincipal? user) =>
        ShopIdentityClaims.Read(user) is { } identity
            ? new ShopCaller(identity.IdentityUserId, identity.Email, identity.EmailVerified)
            : null;

    public static string CorrelationIdOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.TraceIdentifier;
    }

    /// <summary>
    /// How many items this key may send in one batch.
    /// </summary>
    /// <remarks>
    /// From the key's own claim, not the request: a partner must not be able to raise its own
    /// batch limit by asking. The fallback is the seller's default for keys issued before the
    /// claim existed.
    /// </remarks>
    public static int BulkMaxItemsOf(ClaimsPrincipal user, int fallback)
    {
        ArgumentNullException.ThrowIfNull(user);

        var claim = user.FindFirst("bulk_max_items")?.Value;

        return int.TryParse(claim, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
