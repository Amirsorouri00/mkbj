using AwesomeAssertions;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;
using EwaysVNext.Presentation.Endpoint.GraphQL.Extensions;
using Xunit;

namespace EwaysVNext.UnitTests;

public class ResultGraphQLExtensionsTests
{
    [Fact]
    public void ThrowIfFailure_Does_Nothing_On_Success()
    {
        var result = Result.Success();

        var act = () => result.ThrowIfFailure();

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfFailure_Throws_The_Mapped_Exception_On_Failure()
    {
        var result = Result.Failure(new Error("VALIDATION_FAILED", "bad input", 400));

        var act = () => result.ThrowIfFailure();

        act.Should().Throw<ValidationGraphQLException>();
    }

    [Fact]
    public void ValueOrThrow_Returns_The_Value_On_Success()
    {
        var result = Result.Success(42);

        result.ValueOrThrow().Should().Be(42);
    }

    [Fact]
    public void ValueOrThrow_Throws_The_Mapped_Exception_On_Failure()
    {
        var result = Result.Failure<int>(new Error("NOT_FOUND", "missing", 404));

        var act = () => result.ValueOrThrow();

        act.Should().Throw<NotFoundGraphQLException>();
    }

    [Fact]
    public void ValueOrThrow_With_Payload_Returns_The_Given_Payload_On_Success()
    {
        var result = Result.Success();
        var payload = new object();

        result.ValueOrThrow(payload).Should().BeSameAs(payload);
    }

    [Fact]
    public void ValueOrThrow_With_Payload_Throws_The_Mapped_Exception_On_Failure()
    {
        var result = Result.Failure(new Error("FORBIDDEN", "no", 403));

        var act = () => result.ValueOrThrow(new object());

        act.Should().Throw<ForbiddenGraphQLException>();
    }

    [Fact]
    public void ValueOrDefault_Returns_The_Value_On_Success()
    {
        var result = Result.Success("value");

        result.ValueOrDefault().Should().Be("value");
    }

    [Fact]
    public void ValueOrDefault_Returns_Null_When_The_Failure_Is_A_404()
    {
        var result = Result.Failure<string>(new Error("NOT_FOUND", "missing", 404));

        result.ValueOrDefault().Should().BeNull();
    }

    [Fact]
    public void ValueOrDefault_Throws_The_Mapped_Exception_For_Non_404_Failures()
    {
        var result = Result.Failure<string>(new Error("VALIDATION_FAILED", "bad input", 400));

        var act = () => result.ValueOrDefault();

        act.Should().Throw<ValidationGraphQLException>();
    }
}
