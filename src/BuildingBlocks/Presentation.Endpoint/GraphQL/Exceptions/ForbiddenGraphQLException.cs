using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class ForbiddenGraphQLException(Error error) : AppGraphQLException(error);
