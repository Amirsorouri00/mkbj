using Mkbj.Outcomes;

namespace Mkbj.Presentation.Endpoint.GraphQL.Exceptions;

public sealed class ValidationGraphQLException(Error error) : AppGraphQLException(error);
