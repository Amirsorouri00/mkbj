using System.Globalization;

namespace EwaysVNext.Outcomes.Extensions;

public static class MessageFormat
{
    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, format, args);
}
