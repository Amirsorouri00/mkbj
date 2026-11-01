using System.Reflection;
using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

/// <summary>
/// A partner's identity comes from its API key and from nowhere else.
/// <para>
/// <see cref="EwaysVNext.Core.Application.PartnerPrincipal"/> is built from the claims the key
/// handler minted, so a partner can only ever act as itself. That holds only while no request
/// shape carries a partner id: the moment one does, a partner could name another in a body and
/// read its orders, and every query filtering on the principal would be filtering on a value the
/// caller supplied.
/// </para>
/// <para>
/// This is a structural rule rather than a review habit because the mistake is invisible at the
/// call site — a handler reading <c>request.PartnerId</c> looks exactly like one reading the
/// principal.
/// </para>
/// </summary>
public sealed class PartnerTenancyArchitectureTests
{
    /// <summary>Names that mark a type as something a caller can fill in.</summary>
    private static readonly string[] RequestMarkers = ["Request", "Input", "Body", "Command"];

    private static readonly string[] InboundKinds = ["Rest", "GraphQL"];

    [Fact]
    public void No_Inbound_Request_Dto_Has_A_PartnerId_Property()
    {
        var inbound = InboundAssemblies();

        inbound.Should().NotBeEmpty("a scan that loads no assembly asserts nothing");

        inbound.SelectMany(assembly => assembly.GetTypes())
            // Contains, not EndsWith: a type named UpdateProfileRequestV2 or
            // CreateOrderRequestProbe is just as bound as one ending in "Request", and the first
            // mutation check caught this test letting exactly that through.
            .Where(type => RequestMarkers.Any(marker => type.Name.Contains(marker, StringComparison.Ordinal)))
            // An Admin-prefixed shape names a partner as a filter, never as the caller: a
            // backoffice user legitimately asks "show me this partner's customers", and their
            // own authority comes from a realm role rather than from the id they passed.
            .Where(type => !type.Name.StartsWith("Admin", StringComparison.Ordinal))
            .Where(type => type.GetProperties()
                .Any(property => property.Name.Equals("PartnerId", StringComparison.OrdinalIgnoreCase)))
            .Select(type => type.FullName)
            .Should().BeEmpty(
                "a partner-facing port takes its partner id from PartnerPrincipal, built from the "
                + "API-key claims (spec §6.2) — never from the request");
    }

    [Fact]
    public void Every_Module_Inbound_Assembly_Is_Scanned()
    {
        var scanned = InboundAssemblies().Select(assembly => assembly.GetName().Name).ToList();

        // Not every module has every inbound kind, so this asserts the ones that do exist are
        // reached rather than demanding a full matrix.
        scanned.Should().Contain("EwaysVNext.Modules.Customer.Adapter.Inbound.Rest");
        scanned.Should().Contain("EwaysVNext.Modules.Partners.Adapter.Inbound.Rest");
        scanned.Should().Contain("EwaysVNext.Modules.Settings.Adapter.Inbound.Rest");
    }

    [Fact]
    public void The_Module_List_Covers_Everything_Built_In_This_Phase() =>
        HexagonalBoundaryTests.ModuleNames.Should().Contain(["Partners", "Settings", "Customer"],
            "a module missing from the list escapes every boundary rule with the build still green");

    private static List<Assembly> InboundAssemblies() =>
        [.. HexagonalBoundaryTests.ModuleNames
            .SelectMany(module => InboundKinds
                .Select(kind => $"EwaysVNext.Modules.{module}.Adapter.Inbound.{kind}"))
            .Select(TryLoad)
            .OfType<Assembly>()];

    private static Assembly? TryLoad(string name)
    {
        try
        {
            return Assembly.Load(name);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
