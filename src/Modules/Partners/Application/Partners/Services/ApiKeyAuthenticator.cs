using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Partners.Application.Partners.Keys;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Inbound;
using EwaysVNext.Modules.Partners.Application.Partners.Ports.Outbound;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Partners.Application.Partners.Services;

/// <summary>
/// Verifies a presented key: parse, look up by prefix, compare the hash, check the key is live
/// and the partner is not suspended.
/// <para>
/// Every refusal returns <c>null</c> and the caller is told nothing more. Distinguishing "no such
/// prefix" from "wrong secret" would let someone enumerate valid prefixes; distinguishing
/// "expired" from "suspended" would tell an ex-partner which of the two happened. The reason goes
/// to the log, where an operator can see it and a caller cannot.
/// </para>
/// </summary>
public sealed class ApiKeyAuthenticator(
    IApiKeyLookup lookup,
    ApiKeyHasher hasher,
    IClock clock,
    ILogger<ApiKeyAuthenticator> logger) : IApiKeyAuthenticator
{
    public async Task<ApiKeyAuthResult?> AuthenticateAsync(string presentedKey, CancellationToken ct = default)
    {
        if (!ApiKeyFormat.TryParse(presentedKey, out var prefix, out var secret))
        {
            // No prefix to name: the string never parsed, so there is nothing safe to log but the
            // fact that something malformed arrived.
            logger.LogInformation("Partner key rejected: malformed");
            return null;
        }

        var credential = await lookup.FindByPrefixAsync(prefix, ct);
        if (credential is null)
            return Rejected(prefix, "unknown_prefix");

        if (!hasher.Matches(secret, credential.SecretHash))
            return Rejected(prefix, "bad_secret");

        var now = clock.UtcNow;

        if (credential.RevokedAt is not null)
            return Rejected(prefix, "revoked");

        // Exclusive: a key that expires at noon does not work at noon.
        if (credential.ExpiresAt is not null && now >= credential.ExpiresAt)
            return Rejected(prefix, "expired");

        // Suspension stops every key the partner holds at once, without anyone having to revoke
        // them one by one and re-issue them later.
        if (!credential.PartnerActive)
            return Rejected(prefix, "suspended");

        return new ApiKeyAuthResult(
            credential.PartnerId,
            credential.KeyId,
            // The principal wants a set for its Has() checks; this is the one place
            // the conversion is worth doing, once per authenticated request.
            new HashSet<string>(credential.KeyScopes, StringComparer.Ordinal),
            credential.RateLimitPerMinute,
            credential.BulkMaxItems);
    }

    /// <summary>
    /// Logs the prefix only — never the secret, which would put a live credential in the log file
    /// of every environment that ever saw it.
    /// </summary>
    private ApiKeyAuthResult? Rejected(string prefix, string reason)
    {
        logger.LogInformation("Partner key {Prefix} rejected: {Reason}", prefix, reason);
        return null;
    }
}
