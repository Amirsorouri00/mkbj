using AwesomeAssertions;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using Xunit;

namespace EwaysVNext.UnitTests;

public class ErrorGraphQLExtensionsTests
{
    [Fact]
    public void ConcurrencyConflict_Code_Maps_To_ConcurrencyConflictGraphQLException()
    {
        var error = new Error("CONCURRENCY_CONFLICT", "conflict", 409);

        var exception = error.ToGraphQLException();

        exception.Should().BeOfType<ConcurrencyConflictGraphQLException>();
        ((AppGraphQLException)exception).Code.Should().Be("CONCURRENCY_CONFLICT");
        exception.Message.Should().Be("conflict");
    }

    [Fact]
    public void Validation_Code_Maps_To_ValidationGraphQLException()
    {
        var error = new Error("VALIDATION_FAILED", "bad input", 400);

        error.ToGraphQLException().Should().BeOfType<ValidationGraphQLException>();
    }

    [Fact]
    public void Unauthorized_Code_Maps_To_UnauthorizedGraphQLException()
    {
        var error = new Error("UNAUTHORIZED", "no", 401);

        error.ToGraphQLException().Should().BeOfType<UnauthorizedGraphQLException>();
    }

    [Fact]
    public void Forbidden_Code_Maps_To_ForbiddenGraphQLException()
    {
        var error = new Error("FORBIDDEN", "no", 403);

        error.ToGraphQLException().Should().BeOfType<ForbiddenGraphQLException>();
    }

    [Fact]
    public void Unrecognized_Code_Falls_Back_To_HttpStatus_404_As_NotFound()
    {
        var error = new Error("CUSTOMER_NOT_FOUND", "missing", 404);

        error.ToGraphQLException().Should().BeOfType<NotFoundGraphQLException>();
    }

    [Fact]
    public void Unrecognized_Code_Falls_Back_To_HttpStatus_409_As_Conflict()
    {
        var error = new Error("DUPLICATE_MOBILE", "taken", 409);

        error.ToGraphQLException().Should().BeOfType<ConflictGraphQLException>();
    }

    [Fact]
    public void Unrecognized_Code_Falls_Back_To_HttpStatus_403_As_Forbidden()
    {
        var error = new Error("SOME_CODE", "no", 403);

        error.ToGraphQLException().Should().BeOfType<ForbiddenGraphQLException>();
    }

    [Fact]
    public void Unrecognized_Code_Falls_Back_To_HttpStatus_401_As_Unauthorized()
    {
        var error = new Error("SOME_CODE", "no", 401);

        error.ToGraphQLException().Should().BeOfType<UnauthorizedGraphQLException>();
    }

    [Fact]
    public void Unrecognized_Code_Falls_Back_To_HttpStatus_400_As_Validation()
    {
        var error = new Error("SOME_CODE", "bad", 400);

        error.ToGraphQLException().Should().BeOfType<ValidationGraphQLException>();
    }

    [Fact]
    public void Unrecognized_Code_And_HttpStatus_Falls_Back_To_A_Plain_Exception()
    {
        var error = new Error("INTERNAL", "boom", 500);

        var exception = error.ToGraphQLException();

        exception.Should().BeOfType<Exception>();
        exception.Message.Should().Be("boom");
    }
}
