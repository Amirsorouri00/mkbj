using Mkbj.Outcomes;

namespace Mkbj.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class ConcurrencyConflictGraphQLException(Error error) : AppGraphQLException(error);
