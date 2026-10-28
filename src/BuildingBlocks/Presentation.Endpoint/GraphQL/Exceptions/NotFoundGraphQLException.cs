using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class NotFoundGraphQLException(Error error) : AppGraphQLException(error);
