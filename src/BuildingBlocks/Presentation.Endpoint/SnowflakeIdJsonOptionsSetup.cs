using EwaysVNext.Presentation.Endpoint.Extensions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Presentation.Endpoint;

internal sealed class SnowflakeIdJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options) =>
        options.SerializerOptions.Converters.Add(new SnowflakeIdJsonConverter());
}
