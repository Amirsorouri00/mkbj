using System.Globalization;

namespace EwaysVNext.Host;

public static class ConfigurationExtensions
{
    public static IConfigurationBuilder AddModuleConfiguration(
        this IConfigurationBuilder configurationBuilder,
        string[] moduleNames,
        string? environmentName = null)
    {
        foreach (var moduleName in moduleNames)
        {
            var folder = Path.Combine("ModuleAppSetting", CapitalizeFirstLetter(moduleName));

            configurationBuilder.AddJsonFile(Path.Combine(folder, $"modules.{moduleName}.json"), optional: false, reloadOnChange: true);

            if (environmentName is not null)
            {
                configurationBuilder.AddJsonFile(
                    Path.Combine(folder, $"modules.{moduleName}.{environmentName}.json"),
                    optional: true,
                    reloadOnChange: true);
            }
        }

        return configurationBuilder;
    }

    private static string CapitalizeFirstLetter(string value) =>
        value.Length == 0 ? value : char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
}
