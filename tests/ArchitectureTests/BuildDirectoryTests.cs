using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

/// <summary>
/// The deployment artefacts are part of the product, so they get the same treatment as code:
/// everything in one place under <c>build/</c>, and no secret ever committed.
/// </summary>
public sealed partial class BuildDirectoryTests
{
    [Fact]
    public void Spec_Build_Layout_Exists() =>
        new[]
        {
            "build/docker/api.Dockerfile",
            "build/compose/docker-compose.yml",
            "build/compose/docker-compose.dev.yml",
            "build/compose/.env.example",
            "build/compose/caddy/Caddyfile",
            "build/keycloak/shop-realm.json",
            "build/keycloak/backoffice-realm.json",
            "build/postgres/init/01-roles.sql",
        }
        .Where(path => !File.Exists(Path.Combine(Root, path))).Should().BeEmpty();

    [Fact]
    public void Root_Docker_Files_Are_Gone() =>
        new[] { "Dockerfile", "Dockerfile.ci", "docker-compose.yml", ".env.example" }
            .Where(path => File.Exists(Path.Combine(Root, path))).Should().BeEmpty();

    [Fact]
    public void Env_Example_Has_No_Values() =>
        EnvLines("build/compose/.env.example").Should().OnlyContain(line => line.EndsWith('='),
            "the template names the variables; a value in it is a secret in git");

    [Theory]
    [InlineData("build/keycloak/shop-realm.json")]
    [InlineData("build/keycloak/backoffice-realm.json")]
    public void Realm_Secrets_Are_Placeholders(string file) =>
        SecretValues(file).Should().OnlyContain(value => PlaceholderPattern().IsMatch(value),
            "a realm export is committed, so every secret in it must be substituted at import time");

    [Fact]
    public void Roles_Script_Contains_No_Literal_Password() =>
        File.ReadAllText(Path.Combine(Root, "build/postgres/init/01-roles.sql"))
            .Should().NotMatchRegex(@"PASSWORD\s+'", "passwords come from the environment via \\getenv");

    private static IEnumerable<string> EnvLines(string file) =>
        File.ReadAllLines(Path.Combine(Root, file))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

    /// <summary>Every JSON string whose key mentions a password or a secret, at any depth.</summary>
    private static IEnumerable<string> SecretValues(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, file)));
        return Walk(document.RootElement).ToList();

        static IEnumerable<string> Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var isSecret = property.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
                                       || property.Name.Contains("secret", StringComparison.OrdinalIgnoreCase);

                        if (isSecret && property.Value.ValueKind == JsonValueKind.String)
                            yield return property.Value.GetString()!;

                        foreach (var nested in Walk(property.Value)) yield return nested;
                    }
                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        foreach (var nested in Walk(item)) yield return nested;
                    break;
            }
        }
    }

    [GeneratedRegex(@"^\$\{[A-Z0-9_]+\}$")]
    private static partial Regex PlaceholderPattern();

    private static string Root
    {
        get
        {
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EwaysVNext.slnx")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the EwaysVNext solution directory.");
        }
    }
}
