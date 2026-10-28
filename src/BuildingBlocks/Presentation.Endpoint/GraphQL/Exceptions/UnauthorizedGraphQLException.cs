using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class UnauthorizedGraphQLException(Error error) : AppGraphQLException(error);
