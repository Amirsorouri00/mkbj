using Mkbj.Presentation.Endpoint.Extensions;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Mkbj.Presentation.Endpoint;

internal sealed class SnowflakeIdJsonOptionsSetup : IConfigureOptions<JsonOptions>
{
    public void Configure(JsonOptions options) =>
        options.SerializerOptions.Converters.Add(new SnowflakeIdJsonConverter());
}
