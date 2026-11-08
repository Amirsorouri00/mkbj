using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using EwaysVNext.Core.Application.Security;
using Xunit;

namespace EwaysVNext.UnitTests.Identity;

/// <summary>
/// The realm exports are configuration that ships in the repository and is imported verbatim, so
/// what they contain is as much a part of the product as any code: password policy, session
/// lifetimes, whether strangers may register, and whether an admin needs a second factor.
/// <para>
/// They are also the one place a committed secret would be invisible — a realm export is long and
/// mostly boilerplate — so every credential-shaped value is asserted to be a <c>${VAR}</c>
/// placeholder that Keycloak substitutes at import.
/// </para>
/// </summary>
public sealed partial class KeycloakRealmExportTests
{
    private const string ShopFile = "Shop-realm.json";
    private const string BackofficeFile = "Backoffice-realm.json";

    private static string Repo(string relative) =>
        Path.Combine(TestPaths.RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static JsonObject Load(string file) =>
        JsonNode.Parse(File.ReadAllText(Repo($"build/keycloak/{file}")))!.AsObject();

    private static JsonObject Client(string file, string clientId) =>
        Load(file)["clients"]!.AsArray()
            .Select(c => c!.AsObject())
            .Single(c => c["clientId"]!.GetValue<string>() == clientId);

    [Theory]
    [InlineData(ShopFile, AdminRoles.ShopRealm)]
    [InlineData(BackofficeFile, AdminRoles.BackofficeRealm)]
    public void Realm_Is_Named_And_Uses_Placeholder_Smtp(string file, string realm)
    {
        var json = Load(file);

        json["realm"]!.GetValue<string>().Should().Be(realm);
        json["enabled"]!.GetValue<bool>().Should().BeTrue();
        json["accessTokenLifespan"]!.GetValue<int>().Should().Be(300);
        json["bruteForceProtected"]!.GetValue<bool>().Should().BeTrue();

        json["smtpServer"]!.AsObject()
            .Select(property => property.Value!.ToString())
            .Where(value => value != "true")
            .Should().OnlyContain(value => value.StartsWith("${KC_SMTP_", StringComparison.Ordinal));
    }

    [Fact]
    public void Shop_Lets_Strangers_Register_But_Makes_Them_Prove_An_Address()
    {
        var json = Load(ShopFile);

        json["registrationAllowed"]!.GetValue<bool>().Should().BeTrue();
        json["registrationEmailAsUsername"]!.GetValue<bool>().Should().BeTrue();
        json["verifyEmail"]!.GetValue<bool>().Should().BeTrue();
        json["resetPasswordAllowed"]!.GetValue<bool>().Should().BeTrue();
        json["duplicateEmailsAllowed"]!.GetValue<bool>().Should().BeFalse(
            "an address identifies the customer, so two accounts must not share one");
    }

    [Fact]
    public void Backoffice_Defines_Exactly_SuperAdmin_And_Staff_And_No_Registration()
    {
        var json = Load(BackofficeFile);

        json["registrationAllowed"]!.GetValue<bool>().Should().BeFalse(
            "an admin account is created in the console, never by whoever finds the login page");

        json["roles"]!["realm"]!.AsArray()
            .Select(role => role!["name"]!.GetValue<string>())
            .Should().BeEquivalentTo([AdminRoles.SuperAdmin, AdminRoles.Staff],
                "a third role here would be authority the permission matrix does not describe");
    }

    [Fact]
    public void Backoffice_Requires_A_Second_Factor_And_A_Longer_Password()
    {
        var json = Load(BackofficeFile);

        json["passwordPolicy"]!.GetValue<string>().Should().Contain("length(14)");
        Load(ShopFile)["passwordPolicy"]!.GetValue<string>().Should().Contain("length(10)");

        json["requiredActions"]!.AsArray()
            .Select(action => action!.AsObject())
            .Should().ContainSingle(action =>
                action["alias"]!.GetValue<string>() == "CONFIGURE_TOTP"
                && action["enabled"]!.GetValue<bool>()
                && action["defaultAction"]!.GetValue<bool>(),
                "every backoffice account sets up TOTP at first login");
    }

    [Fact]
    public void Backoffice_Sessions_Expire_Sooner_Than_Shop_Sessions()
    {
        var shop = Load(ShopFile);
        var backoffice = Load(BackofficeFile);

        backoffice["ssoSessionIdleTimeout"]!.GetValue<int>()
            .Should().BeLessThan(shop["ssoSessionIdleTimeout"]!.GetValue<int>());
        backoffice["ssoSessionMaxLifespan"]!.GetValue<int>()
            .Should().BeLessThan(shop["ssoSessionMaxLifespan"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(ShopFile, "modular-monolith")]
    [InlineData(BackofficeFile, "backoffice-console")]
    public void Client_Is_Public_Pkce_With_Placeholder_Redirects(string file, string clientId)
    {
        var client = Client(file, clientId);

        client["publicClient"]!.GetValue<bool>().Should().BeTrue();
        client["standardFlowEnabled"]!.GetValue<bool>().Should().BeTrue();
        client["attributes"]!["pkce.code.challenge.method"]!.GetValue<string>().Should().Be("S256");

        client["directAccessGrantsEnabled"]!.GetValue<bool>().Should().BeFalse(
            "a public client that accepts a password grant hands the password to the browser");

        client["redirectUris"]!.AsArray().Should().NotBeEmpty();
        client["redirectUris"]!.AsArray().Select(uri => uri!.GetValue<string>())
            .Should().OnlyContain(uri => uri.StartsWith("${", StringComparison.Ordinal),
                "a redirect URI baked into the repository points at someone else's deployment");
    }

    [Theory]
    [InlineData(ShopFile)]
    [InlineData(BackofficeFile)]
    public void Export_Contains_No_Secrets_Or_Users(string file)
    {
        var json = Load(file);

        json.ContainsKey("users").Should().BeFalse("accounts are created in the console, not in git");

        Walk(json).Where(pair =>
                pair.Key.Contains("secret", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Contains("password", StringComparison.OrdinalIgnoreCase))
            .Where(pair => pair.Key != "passwordPolicy")
            .Select(pair => $"{pair.Key}={pair.Value}")
            .Should().OnlyContain(entry => entry.Contains("${", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ShopFile)]
    [InlineData(BackofficeFile)]
    public void Every_Client_Requests_The_Basic_Scope(string file)
    {
        // Keycloak's `basic` client scope is what puts `sub` in an access token. Without it the
        // token is otherwise perfectly valid — correct issuer, correct signature, correct roles —
        // and the host rejects every one of them, because it requires a subject. The response is a
        // bare 401 with no body and no log line naming the cause, so the realm looks fine, the
        // login looks fine, and nothing works.
        //
        // Both exports shipped without it. This is the guard that says so.
        var json = JsonNode.Parse(File.ReadAllText(Repo($"build/keycloak/{file}")))!.AsObject();

        var clients = json["clients"]!.AsArray();
        clients.Should().NotBeEmpty("a realm with no clients cannot issue a token at all");

        foreach (var client in clients)
        {
            var scopes = client!["defaultClientScopes"]!.AsArray()
                .Select(scope => scope!.GetValue<string>())
                .ToList();

            scopes.Should().Contain(
                "basic",
                "{0} in {1} would otherwise issue access tokens with no sub claim, which the host refuses",
                client["clientId"]!.GetValue<string>(),
                file);
        }
    }

    [Fact]
    public void Production_Compose_Fails_Fast_On_Missing_Smtp()
    {
        var compose = File.ReadAllText(Repo("build/compose/docker-compose.yml"));

        // KC_SMTP_USER and KC_SMTP_PASSWORD are deliberately NOT in this list. `${VAR:?}` refuses
        // an empty value as well as an unset one, so requiring them would make an unauthenticated
        // relay impossible to configure — and an unauthenticated relay is an ordinary thing to
        // have, including in tools/smoke, which uses mailpit. The variables below are the ones
        // that actually carry the intent: a deployment that has not said where mail goes, from
        // whom, on which port, under which transport security, must refuse to start rather than
        // accept registrations whose verification email silently goes nowhere.
        foreach (var variable in new[]
                 {
                     "KC_SMTP_HOST", "KC_SMTP_PORT", "KC_SMTP_FROM",
                     "KC_SMTP_FROM_DISPLAY_NAME", "KC_SMTP_STARTTLS",
                 })
        {
            compose.Should().MatchRegex($@"\$\{{{variable}:\?[^}}]+\}}",
                $"a deployment with no {variable} must refuse to start, not send nothing silently");
        }
    }

    [Fact]
    public void Production_Compose_Passes_Every_Placeholder_The_Exports_Reference()
    {
        // Keycloak substitutes ${VAR} at --import-realm from its own environment. A placeholder
        // the compose file never passes is substituted with nothing, so the realm imports with a
        // blank SMTP host or an empty redirect URI and the failure shows up as "the password
        // reset email never arrives" rather than as a startup error.
        var compose = File.ReadAllText(Repo("build/compose/docker-compose.yml"));

        var referenced = new[] { ShopFile, BackofficeFile }
            .SelectMany(file => PlaceholderPattern().Matches(File.ReadAllText(Repo($"build/keycloak/{file}"))))
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        referenced.Should().NotBeEmpty("the exports are placeholder-driven");

        referenced.Where(variable => !compose.Contains($"{variable}:", StringComparison.Ordinal))
            .Should().BeEmpty("every placeholder must be passed to the keycloak service");
    }

    [GeneratedRegex(@"\$\{([A-Z0-9_]+)\}")]
    private static partial Regex PlaceholderPattern();

    /// <summary>Every string-valued property at any depth, as (name, value).</summary>
    private static IEnumerable<KeyValuePair<string, string>> Walk(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var property in obj)
                {
                    if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                        yield return new KeyValuePair<string, string>(property.Key, text);
                    else
                        foreach (var nested in Walk(property.Value))
                            yield return nested;
                }

                break;

            case JsonArray array:
                foreach (var nested in array.SelectMany(Walk))
                    yield return nested;

                break;
        }
    }
}
