using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;

/// <summary>
/// A temporary failure (503). It must be its own type so the filter keeps its code and its
/// <c>retryable</c> flag; without it a retryable error falls through to the generic masking branch
/// and reaches the client as a permanent INTERNAL.
/// </summary>
public sealed class ServiceUnavailableGraphQLException(Error error) : AppGraphQLException(error);
