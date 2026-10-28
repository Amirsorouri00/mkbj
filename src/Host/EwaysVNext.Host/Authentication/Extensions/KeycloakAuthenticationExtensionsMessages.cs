namespace EwaysVNext.Host.Authentication.Extensions;

internal static class KeycloakAuthenticationExtensionsMessages
{
    public const string MissingConfigurationSection = "Missing 'Keycloak' configuration section.";
    public const string NotAKeycloakAccessToken = "Token is not a Keycloak access token.";
    public const string UnexpectedClient = "Token was not issued to the expected client for this realm.";
    public const string MissingSubject = "Token is missing a subject.";
    public const string MalformedRealmAccessClaim = "Token has a malformed realm_access claim.";
    public const string MissingRequiredRoleFormat = "Token lacks the '{0}' role required for the '{1}' realm.";
    public const string RoutingRejectedMissingOrMalformedHeaderFormat = "Tenant routing rejected the request: Authorization header is missing or is not 'Bearer <token>' (header length {0}).";
    public const string RoutingRejectedEmptyToken = "Tenant routing rejected the request: Authorization header is 'Bearer' with an empty token.";
    public const string RoutingRejectedUnparseableToken = "Tenant routing rejected the request: the bearer token is not a well-formed JWT.";
    public const string RoutingRejectedUnknownIssuerFormat = "Tenant routing rejected the request: token issuer '{0}' does not match any configured realm issuer ({1}).";
    public const string RoutingRejectedException = "Tenant routing rejected the request: an exception was thrown while parsing the bearer token.";
    public const string JwtValidationFailed = "JWT validation failed for tenant {Tenant}: {ExceptionType}";
}
