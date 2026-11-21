using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace Mkbj.Host.OpenApi;

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
        var path = context.Description.RelativePath?.TrimEnd('/');
        if (context.Description.HttpMethod == "POST" && path is "api/v1/orders" or "api/v1/orders/bulk")
        {
            var bulk = path.EndsWith("/bulk", StringComparison.Ordinal);
            var request = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Required = new HashSet<string> { "external_order_id", "external_customer_id", "amount", "currency", "customer" },
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["external_order_id"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Your stable unique order reference. Reuse unchanged on retries." },
                    ["external_customer_id"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    ["amount"] = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = @"^\d+\.\d{2}$", Description = "Exact total as a two-decimal string." },
                    ["currency"] = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^[A-Z]{3}$" },
                    ["customer"] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Description = "Email and phone are required for a new customer. An existing customer may use an empty object.",
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["email"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["phone"] = new OpenApiSchema { Type = JsonSchemaType.String },
                            ["full_name"] = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                    },
                },
            };
            var example = JsonNode.Parse("""
                {"external_order_id":"scalar-order-001","external_customer_id":"scalar-customer-001","amount":"500.00","currency":"USD","customer":{"email":"buyer@example.com","phone":"+6591234567","full_name":"Alex Tan"}}
                """)!;
            operation.Summary = bulk ? "Create orders and invoices in a batch" : "Create an order and invoice for an exact amount";
            operation.Description = "Send a partner API key with orders:create scope (plus customers:write to create a customer). MKBJ composes real product lines totalling the requested amount, resolves or creates the customer, and atomically saves the paid order and its invoice. "
                + "Save data.id and data.invoice.id from the response. Retrieve the invoice using GET /api/v1/invoices/{id}, or download it using GET /api/v1/invoices/{id}/pdf with invoices:read scope. "
                + "The issue date is captured at creation; downloading later does not change it. Retry the SAME external_order_id after a timeout. Matching retries return the original order; changed amount, currency or customer identity returns 409. Idempotency-Key is not used. "
                + "Every authenticated submission has a durable X-Request-Id visible under API requests in Backoffice. Bulk items commit independently.";
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new()
                    {
                        Schema = bulk ? new OpenApiSchema { Type = JsonSchemaType.Object, Required = new HashSet<string> { "items" }, Properties = new Dictionary<string, IOpenApiSchema> { ["items"] = new OpenApiSchema { Type = JsonSchemaType.Array, Items = request } } } : request,
                        Example = bulk ? new JsonObject { ["items"] = new JsonArray(example) } : example,
                    },
                },
            };
            Add(operation, "201", "Order, payment and invoice created. The response contains data.id, data.invoice.id, document JSON and PDF route.");
            Add(operation, "200", "Original order returned for an idempotent retry (Idempotent-Replay: true), or bulk results.");
            Add(operation, "409", "The external order ID was already used with different amount, currency or customer identity.");
            Add(operation, "422", "The requested amount cannot be composed from currently priced, sellable products.");
            Add(operation, "503", "Outcome uncertain or dependency unavailable. Retry with the same external order ID.");
            foreach (var response in operation.Responses!.Values.OfType<OpenApiResponse>())
            {
                response.Headers!["X-Request-Id"] = new OpenApiHeader { Description = "Durable submission receipt ID (after authentication and receipt persistence).", Schema = new OpenApiSchema { Type = JsonSchemaType.String } };
            }
        }

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
