using EwaysVNext.Domain.Common;

namespace EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

/// <summary>
/// Provider-neutral record of the gateway attempt a BankTransaction retains after a
/// payment link is created. Callback and follow-up operations dispatch only to its provider.
/// </summary>
public sealed class GatewayAttempt : ValueObject
{
    public string ProviderName { get; }
    public long RequestId { get; }
    public string Token { get; }
    public DateTimeOffset RequestedAt { get; }
    public string? ProviderReference { get; }
    /// <summary>The bank confirmed this attempt; a later callback for it is a duplicate.</summary>
    public bool IsConfirmed { get; }

    private GatewayAttempt(string providerName, long requestId, string token, DateTimeOffset requestedAt,
        string? providerReference, bool isConfirmed)
    {
        ProviderName = providerName;
        RequestId = requestId;
        Token = token;
        RequestedAt = requestedAt;
        ProviderReference = providerReference;
        IsConfirmed = isConfirmed;
    }

    public static GatewayAttempt Create(string providerName, long requestId, string token, DateTimeOffset requestedAt,
        string? providerReference = null)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new DomainException("GATEWAY_ATTEMPT_PROVIDER_REQUIRED", "A gateway attempt requires its provider name.");
        if (requestId <= 0)
            throw new DomainException("GATEWAY_ATTEMPT_REQUEST_ID_INVALID", "A gateway attempt requires a positive request ID.");
        if (string.IsNullOrWhiteSpace(token))
            throw new DomainException("GATEWAY_ATTEMPT_TOKEN_REQUIRED", "A gateway attempt requires its provider token.");
        if (requestedAt == default)
            throw new DomainException("GATEWAY_ATTEMPT_REQUESTED_AT_REQUIRED", "A gateway attempt requires its request time.");
        EnsureReference(providerReference, allowNull: true);

        return new GatewayAttempt(providerName, requestId, token, requestedAt, providerReference, isConfirmed: false);
    }

    public GatewayAttempt WithProviderReference(string providerReference)
    {
        EnsureReference(providerReference, allowNull: false);
        return new GatewayAttempt(ProviderName, RequestId, Token, RequestedAt, providerReference, IsConfirmed);
    }

    public GatewayAttempt WithConfirmation(string providerReference)
    {
        EnsureReference(providerReference, allowNull: false);
        return new GatewayAttempt(ProviderName, RequestId, Token, RequestedAt, providerReference, isConfirmed: true);
    }

    private static void EnsureReference(string? providerReference, bool allowNull)
    {
        if (providerReference is null ? !allowNull : string.IsNullOrWhiteSpace(providerReference))
            throw new DomainException("GATEWAY_ATTEMPT_REFERENCE_INVALID", "A supplied provider reference cannot be blank.");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ProviderName;
        yield return RequestId;
        yield return Token;
        yield return RequestedAt;
        yield return ProviderReference;
        yield return IsConfirmed;
    }
}
