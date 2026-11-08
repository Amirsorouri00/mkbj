using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace EwaysVNext.Host.OpenApi;

/// <summary>
/// Describes the partner API the way a partner actually uses it.
/// </summary>
/// <remarks>
/// <para>
/// The partner contract is the public face of this system: somebody else's engineers read it and
/// write code against it, often without being able to ask a question. So it documents the whole
/// shape of an interaction — not only the happy path, but the refusals a caller has to handle,
/// the header that tells a retry from a first attempt, and the correlation id they will quote in
/// a support ticket.
/// </para>
/// <para>
/// Both a document and an operation transformer. The scheme belongs to the document; which
/// operations require it is decided per operation, by reading the authorization metadata rather
/// than by matching on route text — a route renamed without updating a list here would otherwise
/// quietly lose its security requirement in the published contract.
/// </para>
/// </remarks>
public sealed class PartnerApiKeyOpenApiTransformer
    : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public const string SchemeName = "PartnerApiKey";

    /// <summary>The correlation id every response carries, named once.</summary>
    private const string CorrelationHeader = "X-Correlation-Id";

    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",

            // Spelled out because the format is not a JWT and a partner reading "bearer" would
            // reasonably assume it was.
            BearerFormat = "pk_<prefix>_<secret>",
            Description =
                "A partner API key, sent as `Authorization: Bearer pk_...`. Keys are issued by "
                + "the seller and carry their own scopes and rate limit.",
        };

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var policies = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Select(data => data.Policy)
            .Where(policy => policy is not null)
            .ToList();

        if (!policies.Any(policy => policy!.StartsWith("partner:", StringComparison.Ordinal)))
            return Task.CompletedTask;

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
            },
        ];

        AddCommonResponses(operation);

        return Task.CompletedTask;
    }

    /// <summary>
    /// The three refusals every partner operation can return.
    /// </summary>
    /// <remarks>
    /// Documented on every operation rather than mentioned once in prose, because a generated
    /// client only knows what the document says: an undocumented 429 becomes an unhandled
    /// exception in somebody else's nightly job.
    /// </remarks>
    private static void AddCommonResponses(OpenApiOperation operation)
    {
        operation.Responses ??= new OpenApiResponses();

        Add(operation, "401", "No key was sent, or it is not a live key.");
        Add(operation, "403", "The key does not grant the scope this operation needs.");
        Add(operation, "429", "The key's rate limit was exceeded. Retry after the stated delay.");

        // On the success responses too: the correlation id is what a partner quotes when
        // something went wrong, and it is most useful on the call that seemed to work.
        foreach (var (_, response) in operation.Responses)
            WithCorrelationHeader(response);
    }

    private static void Add(OpenApiOperation operation, string status, string description)
    {
        if (operation.Responses!.ContainsKey(status)) return;

        operation.Responses[status] = WithCorrelationHeader(new OpenApiResponse
        {
            Description = description,
        });
    }

    private static IOpenApiResponse WithCorrelationHeader(IOpenApiResponse response)
    {
        if (response is not OpenApiResponse concrete) return response;

        concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();

        concrete.Headers[CorrelationHeader] = new OpenApiHeader
        {
            Description =
                "Echoes the request's correlation id, or the one this system assigned. Quote it "
                + "in a support request.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
        };

        return concrete;
    }
}
