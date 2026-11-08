namespace EwaysVNext.Presentation.Endpoint.GraphQL;

/// <summary>
/// What one GraphQL request may cost.
/// </summary>
/// <remarks>
/// <para>
/// One endpoint, one executor, so these apply to the whole schema — admin fields and anonymous
/// storefront fields alike. That is not a compromise: the storefront queries are shallower than
/// the limit, and a per-field depth rule would be a second place for the number to live.
/// </para>
/// <para>
/// Depth matters more than it looks on an anonymous endpoint. Without it, a nested query over a
/// self-referencing type — a category and its parent and its parent — is a few hundred bytes
/// that asks the database for an unbounded amount of work. Introspection is exempt because the
/// schema document is legitimately deep and reading it costs nothing.
/// </para>
/// </remarks>
public static class GraphQLLimits
{
    public const int MaxDepth = 10;

    public const int MaxCost = 20_000;
}
