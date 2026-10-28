using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class ConflictGraphQLException(Error error) : AppGraphQLException(error);
