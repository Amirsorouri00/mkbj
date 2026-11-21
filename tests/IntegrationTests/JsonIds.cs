using System.Globalization;
using System.Text.Json;

namespace Mkbj.IntegrationTests;

internal static class JsonIds
{
    // GetString deliberately rejects a numeric token: browsers cannot preserve Snowflake numbers.
    public static long ReadId(this JsonElement value) => long.Parse(value.GetString()!, CultureInfo.InvariantCulture);
}
