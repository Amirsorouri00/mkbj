using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;
using HotChocolate;
using Xunit;
using Error = EwaysVNext.Outcomes.Error;

namespace EwaysVNext.UnitTests;

public class GraphQLErrorFilterTests
{
    private readonly GraphQLErrorFilter _filter = new();

    [Fact]
    public void AppGraphQLException_Preserves_The_Public_Error_Details()
    {
        var exception = new ValidationGraphQLException(new Error("VALIDATION", "bad input", 400));
        var error = ErrorBuilder.New()
            .SetMessage("original message")
            .SetException(exception)
            .Build();

        var result = _filter.OnError(error);

        result.Code.Should().Be("VALIDATION");
        result.Message.Should().Be("bad input");
        result.Extensions!["httpStatus"].Should().Be(400);
    }

    [Fact]
    public void Unrecognized_Exception_Is_Masked_As_A_Generic_Internal_Error()
    {
        var error = ErrorBuilder.New()
            .SetMessage("original message")
            .SetException(new InvalidOperationException("boom"))
            .Build();

        var result = _filter.OnError(error);

        result.Message.Should().Be("An unexpected error occurred.");
        result.Code.Should().Be(Error.Internal.Code);
    }

    [Fact]
    public void No_Exception_Is_Passed_Through_Unchanged()
    {
        var error = ErrorBuilder.New()
            .SetMessage("original message")
            .SetCode("AUTH_NOT_AUTHENTICATED")
            .Build();

        var result = _filter.OnError(error);

        result.Message.Should().Be("original message");
        result.Code.Should().Be("AUTH_NOT_AUTHENTICATED");
    }
}
