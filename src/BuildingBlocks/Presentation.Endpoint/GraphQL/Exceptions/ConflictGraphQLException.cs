using Mkbj.Outcomes;

namespace Mkbj.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class ConflictGraphQLException(Error error) : AppGraphQLException(error);
