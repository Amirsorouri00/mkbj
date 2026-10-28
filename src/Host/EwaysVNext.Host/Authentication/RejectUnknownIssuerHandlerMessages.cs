namespace EwaysVNext.Host.Authentication;

internal static class RejectUnknownIssuerHandlerMessages
{
    public const string UnknownIssuer = "Bearer token issuer is missing, unparseable, or does not match a configured realm.";
}
