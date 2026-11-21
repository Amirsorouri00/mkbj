using Mkbj.Outcomes;

namespace Mkbj.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class NotFoundGraphQLException(Error error) : AppGraphQLException(error);
