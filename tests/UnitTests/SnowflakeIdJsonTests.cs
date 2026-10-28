using System.Text.Json;
using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Contracts.Products;
using EwaysVNext.Presentation.Endpoint;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EwaysVNext.UnitTests;

public class SnowflakeIdJsonTests
{
    private const long SampleId = 96521147829043201;

    private static JsonSerializerOptions HttpJsonOptions()
    {
        var services = new ServiceCollection().AddOptions();
        services.AddEndpoints(typeof(SnowflakeIdJsonTests).Assembly);

        return services.BuildServiceProvider().GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
    }

    [Fact]
    public void SnowflakeId_Fields_Are_Written_As_Strings_And_Plain_Longs_Stay_Numbers()
    {
        var sample = new Sample(SampleId, SampleId + 1, null, [SampleId + 2], SampleId + 3, 12, 7, 9.5m);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { Data = sample }, HttpJsonOptions()));
        var data = json.RootElement.GetProperty("data");

        data.GetProperty("id").GetString().Should().Be("96521147829043201");
        data.GetProperty("customerId").GetString().Should().Be("96521147829043202");
        data.GetProperty("parentId").ValueKind.Should().Be(JsonValueKind.Null);
        data.GetProperty("tagIds")[0].GetString().Should().Be("96521147829043203");
        data.GetProperty("unmarkedId").GetInt64().Should().Be(SampleId + 3);
        data.GetProperty("totalCount").GetInt64().Should().Be(12);
        data.GetProperty("cityId").GetInt32().Should().Be(7);
        data.GetProperty("price").GetDecimal().Should().Be(9.5m);
    }

    [Theory]
    [InlineData("""{"id":"96521147829043201","customerId":96521147829043201}""")]
    [InlineData("""{"id":96521147829043201,"customerId":"96521147829043201"}""")]
    public void Snowflake_Ids_Are_Read_From_Either_A_String_Or_A_Number(string body)
    {
        var request = JsonSerializer.Deserialize<RequestSample>(body, HttpJsonOptions())!;

        request.Id.Value.Should().Be(SampleId);
        request.CustomerId.Value.Should().Be(SampleId);
    }

    [Fact]
    public void The_Response_Dtos_Serialize_Their_Snowflake_Ids_As_Strings()
    {
        var options = HttpJsonOptions();

        JsonSerializer.Serialize(new ProductSnapshot(SampleId, "Pen", 1m, "IRR", 3), options)
            .Should().Contain("\"id\":\"96521147829043201\"");
        JsonSerializer.Serialize(new ReservationSnapshot(SampleId, SampleId + 1, 2, TestTime.Now), options)
            .Should().Contain("\"reservationId\":\"96521147829043201\"").And.Contain("\"productId\":\"96521147829043202\"");
    }

    private sealed record Sample(
        SnowflakeId Id,
        SnowflakeId CustomerId,
        SnowflakeId? ParentId,
        IReadOnlyList<SnowflakeId> TagIds,
        long UnmarkedId,
        long TotalCount,
        int CityId,
        decimal Price);

    private sealed record RequestSample(SnowflakeId Id, SnowflakeId CustomerId);
}
