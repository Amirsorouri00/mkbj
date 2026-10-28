using System.Reflection;
using AuthorizationService.Endpoints.Permissions;
using AwesomeAssertions;
using EwaysVNext.Presentation.Endpoint.Security;
using HotChocolate.Authorization;
using Xunit;

namespace EwaysVNext.UnitTests.Identity;

/// <summary>
/// The RBAC GraphQL edits the permission matrix itself, so it is the one surface where a missing
/// gate is self-escalating: an ungated <c>createPermission</c> lets any authenticated caller grant
/// themselves <c>partners.manage</c>. Before this test the mutations carried a bare
/// <c>[Authorize]</c> — satisfied by any logged-in customer — and the queries carried nothing at
/// all, so the whole matrix was world-readable.
/// <para>
/// Asserted by reflection rather than by reading the files, because the failure mode is a
/// resolver someone adds later and forgets to annotate.
/// </para>
/// </summary>
public sealed class RbacGraphQlGatingTests
{
    private static IEnumerable<Type> ResolverTypes(string suffix) =>
        typeof(PermissionMutations).Assembly.GetTypes()
            .Where(t => t.IsClass && t.IsPublic && t.Name.EndsWith(suffix, StringComparison.Ordinal));

    private static IEnumerable<MethodInfo> Resolvers(string suffix) =>
        ResolverTypes(suffix).SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Where(m => m.DeclaringType?.Name.EndsWith(suffix, StringComparison.Ordinal) == true);

    private static string? PolicyOf(MethodInfo method) =>
        method.GetCustomAttribute<AuthorizeAttribute>()?.Policy
        ?? method.DeclaringType?.GetCustomAttribute<AuthorizeAttribute>()?.Policy;

    [Fact]
    public void Every_Rbac_Mutation_Requires_A_Super_Admin()
    {
        var resolvers = Resolvers("Mutations").ToList();
        resolvers.Should().NotBeEmpty("the reflection would otherwise assert nothing");

        resolvers.Where(m => PolicyOf(m) != AuthPolicies.AdminSuper)
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .Should().BeEmpty("editing the permission matrix is a super-admin act");
    }

    [Fact]
    public void Every_Rbac_Query_Requires_At_Least_Staff()
    {
        var resolvers = Resolvers("Queries").ToList();
        resolvers.Should().NotBeEmpty("the reflection would otherwise assert nothing");

        resolvers.Where(m => PolicyOf(m) is not (AuthPolicies.AdminStaff or AuthPolicies.AdminSuper))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .Should().BeEmpty("who may do what is not public information");
    }
}
