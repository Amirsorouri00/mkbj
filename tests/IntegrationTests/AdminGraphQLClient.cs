using System.Net.Http.Json;
using System.Text.Json;

namespace EwaysVNext.IntegrationTests;

/// <summary>Thin helper for POSTing operations to <c>/graphql</c> in tests.</summary>
internal static class AdminGraphQLClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public sealed record GraphQLRequest(string Query, object? Variables = null);
    public sealed record GraphQLError(string Message, GraphQLErrorExtensions? Extensions);
    public sealed record GraphQLErrorExtensions(string? Code);
    public sealed record GraphQLResponse<TData>(TData? Data, GraphQLError[]? Errors);

    public static Task<HttpResponseMessage> PostAsync(
        HttpClient client, string query, object? variables = null, CancellationToken ct = default) =>
        client.PostAsJsonAsync("/graphql", new GraphQLRequest(query, variables), JsonOptions, ct);

    public static async Task<GraphQLResponse<TData>> PostForResultAsync<TData>(
        HttpClient client, string query, object? variables = null, CancellationToken ct = default)
    {
        var response = await PostAsync(client, query, variables, ct);
        response.EnsureSuccessStatusCode(); // GraphQL errors are HTTP 200 with an "errors" array
        var body = await response.Content.ReadFromJsonAsync<GraphQLResponse<TData>>(JsonOptions, ct);
        return body ?? new GraphQLResponse<TData>(default, null);
    }

    public static async Task<string?> FirstErrorCodeAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        // Deliberately no EnsureSuccessStatusCode() here: most GraphQL errors come back as
        // HTTP 200 with an "errors" array, but this server maps pure-authorization failures
        // (AUTH_NOT_AUTHENTICATED / AUTH_NOT_AUTHORIZED) to a real HTTP 401/403 while still
        // returning the same well-formed GraphQL error body — which is exactly what the
        // callers of this helper are checking for.
        var body = await response.Content.ReadFromJsonAsync<GraphQLResponse<JsonElement>>(JsonOptions, ct);
        return body?.Errors is { Length: > 0 } errors ? errors[0].Extensions?.Code : null;
    }
}
