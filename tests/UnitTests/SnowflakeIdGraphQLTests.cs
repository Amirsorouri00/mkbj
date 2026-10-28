using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Presentation.Endpoint.GraphQL;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

public class SnowflakeIdGraphQLTests
{
    private const long SampleId = 96521147829043201;

    private static async Task<IRequestExecutor> ExecutorAsync()
    {
        var services = new ServiceCollection().AddLogging();
        return await services.AddGraphQLBootstrap()
            .AddTypeExtension(typeof(SampleQueries))
            .AddTypeExtension(typeof(SampleMutations))
            .BuildRequestExecutorAsync();
    }

    private static async Task<JsonElement> ExecuteAsync(string document)
    {
        var result = await (await ExecutorAsync()).ExecuteAsync(document);
        using var json = JsonDocument.Parse(result.ToJson());
        json.RootElement.TryGetProperty("errors", out var errors).Should().BeFalse(errors.ToString());
        return json.RootElement.GetProperty("data").Clone();
    }

    [Fact]
    public async Task SnowflakeId_Fields_Are_Typed_As_ID_And_Other_Numbers_Keep_Their_Type()
    {
        var sdl = (await ExecutorAsync()).Schema.ToString();

        sdl.Should().Contain("snowflakeSample(customerId: ID!): SnowflakeSample!");
        sdl.Should().Contain("id: ID!").And.Contain("parentId: ID").And.Contain("tagIds: [ID!]!");
        sdl.Should().Contain("unmarkedId: Long!").And.Contain("totalCount: Long!").And.Contain("cityId: Int!");
        sdl.Should().Contain("input SnowflakeSampleInput {").And.Contain("customerId: ID!");
    }

    [Fact]
    public async Task Snowflake_Ids_Are_Returned_As_Strings()
    {
        var data = await ExecuteAsync(
            """{ snowflakeSample(customerId: "96521147829043201") { id customerId parentId tagIds unmarkedId totalCount cityId } }""");
        var sample = data.GetProperty("snowflakeSample");

        sample.GetProperty("id").GetString().Should().Be("96521147829043201");
        sample.GetProperty("customerId").GetString().Should().Be("96521147829043201");
        sample.GetProperty("parentId").ValueKind.Should().Be(JsonValueKind.Null);
        sample.GetProperty("tagIds")[0].GetString().Should().Be("96521147829043202");
        sample.GetProperty("unmarkedId").GetInt64().Should().Be(SampleId + 2);
        sample.GetProperty("totalCount").GetInt64().Should().Be(12);
        sample.GetProperty("cityId").GetInt32().Should().Be(7);
    }

    [Theory]
    [InlineData("""mutation { echoSnowflake(input: { customerId: "96521147829043201" }) { customerId } }""")]
    [InlineData("""mutation { echoSnowflake(input: { customerId: 96521147829043201 }) { customerId } }""")]
    public async Task Snowflake_Id_Inputs_Accept_A_String_Or_A_Number(string document)
    {
        var data = await ExecuteAsync(document);

        data.GetProperty("echoSnowflake").GetProperty("customerId").GetString().Should().Be("96521147829043201");
    }

    [Fact]
    public async Task A_Non_Numeric_Snowflake_Id_Is_Rejected_As_A_Validation_Error()
    {
        var result = await (await ExecutorAsync()).ExecuteAsync("""{ snowflakeSample(customerId: "abc") { id } }""");

        result.ToJson().Should().Contain("errors").And.NotContain("96521147829043201");
    }

    public sealed record SnowflakeSample(
        SnowflakeId Id,
        SnowflakeId CustomerId,
        SnowflakeId? ParentId,
        IReadOnlyList<SnowflakeId> TagIds,
        long UnmarkedId,
        long TotalCount,
        int CityId);

    public sealed record SnowflakeSampleInput(SnowflakeId CustomerId);

    public sealed record EchoSnowflakePayload(SnowflakeId CustomerId);

    [ExtendObjectType(typeof(Query))]
    public static class SampleQueries
    {
        public static SnowflakeSample SnowflakeSample(SnowflakeId customerId) =>
            new(SampleId, customerId, null, [SampleId + 1], SampleId + 2, 12, 7);
    }

    [ExtendObjectType(typeof(Mutation))]
    public static class SampleMutations
    {
        public static EchoSnowflakePayload EchoSnowflake(SnowflakeSampleInput input) => new(input.CustomerId);
    }
}
