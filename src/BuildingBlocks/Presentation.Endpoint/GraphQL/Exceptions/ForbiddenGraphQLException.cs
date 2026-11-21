using Mkbj.Outcomes;

namespace Mkbj.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class ForbiddenGraphQLException(Error error) : AppGraphQLException(error);
