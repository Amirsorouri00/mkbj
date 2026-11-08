namespace EwaysVNext.Presentation.Endpoint.Security;

/// <summary>
/// The named rate-limit policies, so an endpoint and the limiter cannot disagree about a string.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>Per backoffice user, for the admin surface.</summary>
    public const string Admin = "admin";

    /// <summary>
    /// The one GraphQL endpoint.
    /// </summary>
    /// <remarks>
    /// It cannot simply use <see cref="Admin"/>: the same endpoint serves the anonymous
    /// storefront, and limiting those callers per subject would mean limiting them all as one.
    /// </remarks>
    public const string GraphQL = "graphql";
}
