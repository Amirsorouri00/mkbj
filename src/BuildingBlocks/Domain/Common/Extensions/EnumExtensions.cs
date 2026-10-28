using System.ComponentModel;
using System.Reflection;

namespace EwaysVNext.Domain.Common.Extensions;

public static class EnumExtensions
{
    public static string GetDescription(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attribute = field?.GetCustomAttribute<DescriptionAttribute>();
        return attribute?.Description ?? string.Empty;
    }

    public static object ToModel(this Enum value) =>
        new { Id = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture), Title = value.GetDescription() };
}
