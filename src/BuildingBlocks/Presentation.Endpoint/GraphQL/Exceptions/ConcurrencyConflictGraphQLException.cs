using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class ConcurrencyConflictGraphQLException(Error error) : AppGraphQLException(error);
