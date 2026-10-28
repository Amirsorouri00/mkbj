using EwaysVNext.Modules.Payment.Application.Domain.ValueObjects;

namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Providers;

/// <summary>Bank-independent boundary message, not a transaction entity.</summary>
public sealed record GatewayLinkRequest(long RequestId, long BankTransactionId, Money Amount, Uri CallbackUrl);

public sealed record GatewayLink(string Token, GatewayRedirect Redirect);
