namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

internal sealed record ReversalRequest(string LoginAccount, long Token);
