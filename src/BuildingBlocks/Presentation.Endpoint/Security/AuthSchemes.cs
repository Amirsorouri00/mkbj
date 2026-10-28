using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace EwaysVNext.Presentation.Endpoint.Security;

/// <summary>
/// Partner keys and human tokens both arrive as <c>Authorization: Bearer …</c>, so something has
/// to tell them apart before either handler tries to parse one. The <c>pk_</c> prefix is what
/// does it (ADR-0021): a partner key is recognisable without a database lookup, and a JWT reader
/// never sees a key it would reject as malformed JWT.
/// </summary>
public static class AuthSchemes
{
    public const string PartnerApiKey = "PartnerApiKey";
    public const string PartnerKeyPrefix = "pk_";

    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// Whether this request carries a partner API key. The scheme word is matched
    /// case-insensitively, as HTTP requires; the key prefix is matched ordinally, because it is
    /// part of a credential and not a word.
    /// </summary>
    public static bool IsPartnerKey(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var header = request.Headers[HeaderNames.Authorization].ToString();

        return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            && header.AsSpan(BearerPrefix.Length).TrimStart()
                .StartsWith(PartnerKeyPrefix, StringComparison.Ordinal);
    }
}
