using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EwaysVNext.Domain.Common;

namespace EwaysVNext.Presentation.Endpoint.Extensions;

public sealed class SnowflakeIdJsonConverter : JsonConverter<SnowflakeId>
{
    public override SnowflakeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? long.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
            : reader.GetInt64();

    public override void Write(Utf8JsonWriter writer, SnowflakeId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
}
