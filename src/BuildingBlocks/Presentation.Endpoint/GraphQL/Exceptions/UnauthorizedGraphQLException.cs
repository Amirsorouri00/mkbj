using Mkbj.Outcomes;

namespace Mkbj.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class UnauthorizedGraphQLException(Error error) : AppGraphQLException(error);
