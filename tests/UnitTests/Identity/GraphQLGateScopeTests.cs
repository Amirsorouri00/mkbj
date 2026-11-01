using System.Reflection;
using AwesomeAssertions;
using HotChocolate.Authorization;
using HotChocolate.Types;
using Xunit;

namespace EwaysVNext.UnitTests.Identity;

/// <summary>
/// A type-level <c>[Authorize]</c> on an <c>[ExtendObjectType]</c> class does not gate only that
/// class's fields — it is merged onto the target type, so it applies to <b>every</b> field of the
/// merged <c>Query</c> or <c>Mutation</c>, including fields contributed by other modules.
/// <para>
/// That is how the admin surface's <c>admin:super</c> gate came to reject a shop customer calling
/// <c>me</c>: a field with no attribute of its own, in another module, was being judged against a
/// policy it had never asked for. The failure reads as "this customer is not authorized",
/// pointing at the shop code rather than at the admin extension that caused it.
/// </para>
/// <para>
/// The remedy is simply to gate each resolver. This test keeps it that way.
/// </para>
/// </summary>
public sealed class GraphQLGateScopeTests
{
    private static IEnumerable<Type> ExtensionTypes() =>
        new[]
        {
            typeof(AuthorizationService.Endpoints.Permissions.PermissionQueries).Assembly,
            typeof(EwaysVNext.Modules.Partners.Adapter.Inbound.GraphQL.PartnerAdminGraphQLQueries).Assembly,
            typeof(EwaysVNext.Modules.Settings.Adapter.Inbound.GraphQL.SettingsAdminGraphQLQueries).Assembly,
            typeof(EwaysVNext.Modules.Customer.Adapter.Inbound.GraphQL.Shop.ShopMeQueries).Assembly,
        }
        .Distinct()
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => type.GetCustomAttribute<ExtendObjectTypeAttribute>() is not null);

    [Fact]
    public void No_Schema_Extension_Gates_At_The_Type_Level()
    {
        var extensions = ExtensionTypes().ToList();

        extensions.Should().NotBeEmpty("the reflection would otherwise assert nothing");

        extensions
            .Where(type => type.GetCustomAttribute<AuthorizeAttribute>() is not null)
            .Select(type => type.Name)
            .Should().BeEmpty(
                "a type-level gate on a schema extension applies to every field of the merged "
                + "type, including other modules' — put it on each resolver instead");
    }

    [Fact]
    public void Every_Schema_Extension_Resolver_Carries_Its_Own_Gate()
    {
        var ungated = ExtensionTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => !method.IsSpecialName)
            .Where(method => method.GetCustomAttribute<AuthorizeAttribute>() is null
                             && method.GetCustomAttribute<AllowAnonymousAttribute>() is null)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();

        ungated.Should().BeEmpty(
            "with type-level gates gone, a resolver without its own is reachable by anyone "
            + "authenticated; mark a deliberately public field [AllowAnonymous]");
    }
}
