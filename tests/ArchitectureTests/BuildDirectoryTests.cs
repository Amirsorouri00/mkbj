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
    private static readonly string[] RequiredArtefacts =
    [
        "build/docker/api.Dockerfile",
        "build/compose/docker-compose.yml",
        "build/compose/docker-compose.dev.yml",
        "build/compose/.env.example",
        "build/compose/caddy/Caddyfile",
        "build/keycloak/Shop-realm.json",
        "build/keycloak/Backoffice-realm.json",
        "build/postgres/init/01-roles.sql",
    ];

    private static readonly string[] SupersededRootFiles =
        ["Dockerfile", "Dockerfile.ci", "docker-compose.yml", ".env.example"];

    [Fact]
    public void Spec_Build_Layout_Exists() =>
        RequiredArtefacts.Where(path => !File.Exists(Path.Combine(Root, path))).Should().BeEmpty();

    [Fact]
    public void Root_Docker_Files_Are_Gone() =>
        SupersededRootFiles.Where(path => File.Exists(Path.Combine(Root, path))).Should().BeEmpty();

    [Fact]
    public void Env_Example_Has_No_Values() =>
        EnvLines("build/compose/.env.example").Should().OnlyContain(line => line.EndsWith('='),
            "the template names the variables; a value in it is a secret in git");

    /// <summary>
    /// Keycloak refuses to start when an import file is not named after the realm inside it
    /// ("File name / realm name mismatch"), and --import-realm makes that a crash loop rather
    /// than a warning. The name is part of the contract, so assert it rather than discovering it
    /// from a container that will not come up.
    /// </summary>
    [Fact]
    public void Realm_Exports_Are_Named_After_The_Realms_They_Contain()
    {
        // Enumerate the directory rather than assert against a path spelled here: Windows
        // matches a path case-insensitively, so a hard-coded name would compare the string in
        // this file against itself and pass over a file actually named differently on disk.
        var mismatched = Directory.GetFiles(Path.Combine(Root, "build", "keycloak"), "*-realm.json")
            .Select(path =>
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                var expected = Path.GetFileName(path).Replace("-realm.json", string.Empty, StringComparison.Ordinal);
                return (File: Path.GetFileName(path), Realm: document.RootElement.GetProperty("realm").GetString(), Expected: expected);
            })
            .Where(x => !string.Equals(x.Realm, x.Expected, StringComparison.Ordinal))
            .ToList();

        mismatched.Should().BeEmpty("Keycloak refuses to start on a file name / realm name mismatch");
    }

    [Theory]
    [InlineData("build/keycloak/Shop-realm.json")]
    [InlineData("build/keycloak/Backoffice-realm.json")]
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
    private static List<string> SecretValues(string file)
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
                        // "passwordPolicy" names the rules a password must satisfy; it is public
                        // configuration, not a credential, and excluding it by name keeps the
                        // substring match from having to understand Keycloak's schema.
                        var isSecret = property.Name is not "passwordPolicy"
                                       && (property.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
                                           || property.Name.Contains("secret", StringComparison.OrdinalIgnoreCase));

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
