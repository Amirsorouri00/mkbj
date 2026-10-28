using EwaysVNext.Outcomes;

namespace EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class ValidationGraphQLException(Error error) : AppGraphQLException(error);
