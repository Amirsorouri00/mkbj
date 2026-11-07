using AwesomeAssertions;
using EwaysVNext.Modules.Sales.Application.Composition;
using NetArchTest.Rules;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

/// <summary>
/// Rules that are specific to Sales, on top of the module-wide ones in
/// <see cref="HexagonalBoundaryTests"/>.
/// </summary>
public sealed class SalesArchitectureTests
{
    private const string CompositionNamespace = "EwaysVNext.Modules.Sales.Application.Composition";
    private const string DomainNamespace = "EwaysVNext.Modules.Sales.Application.Domain";

    /// <summary>
    /// The composition engine is pure, and the build enforces it.
    /// </summary>
    /// <remarks>
    /// Its seed is persisted so a composition can be replayed and audited years later. That is
    /// only true while the engine's output depends on nothing but its inputs — one database read,
    /// one clock call or one log line with a timestamp in it, and "we can reproduce what the
    /// customer was sold" stops being true. Purity here is an audit property, not a style.
    /// </remarks>
    [Fact]
    public void Sales_Composition_Has_No_IO_Or_Port_Dependencies() =>
        Types.InAssembly(typeof(CompositionEngine).Assembly)
            .That().ResideInNamespace(CompositionNamespace)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore", "System.Net", "System.IO", "System.Data",
                "EwaysVNext.Modules.Sales.Application.Ports", "EwaysVNext.Persistence",
                "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Logging")
            .GetResult().IsSuccessful.Should().BeTrue();

    /// <summary>
    /// The aggregates know nothing about how they are loaded or what is done with them.
    /// </summary>
    [Fact]
    public void Sales_Domain_Never_References_Ports_Or_Services() =>
        Types.InAssembly(typeof(CompositionEngine).Assembly)
            .That().ResideInNamespace(DomainNamespace)
            .ShouldNot().HaveDependencyOnAny(
                "EwaysVNext.Modules.Sales.Application.Ports",
                "EwaysVNext.Modules.Sales.Application.Services")
            .GetResult().IsSuccessful.Should().BeTrue();

    /// <summary>
    /// The seller is not GST-registered, so nothing it issues may be called a tax invoice.
    /// </summary>
    /// <remarks>
    /// In Singapore the phrase is reserved for a GST-registered supplier, and using it would be a
    /// misrepresentation to the buyer and to IRAS — not a wording preference. A source scan,
    /// because the phrase could appear in a PDF template, a wording constant or a comment that
    /// someone later copies into one.
    /// </remarks>
    [Fact]
    public void Sales_Source_Never_Calls_An_Invoice_A_Tax_Invoice() =>
        Directory.EnumerateFiles(
                Path.Combine(SourceRoot(), "Modules", "Sales"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Where(file => File.ReadAllText(file).Contains("Tax Invoice", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty();

    /// <summary>
    /// No partner request shape may carry a partner id.
    /// </summary>
    /// <remarks>
    /// The authenticated key says which partner is calling. The moment a request can name one,
    /// every partner-scoped query is one forgotten filter away from serving another partner's
    /// orders — and the mistake would look like ordinary parameter passing in review.
    /// </remarks>
    [Fact]
    public void Partner_Request_Dtos_Never_Accept_A_Partner_Id() =>
        typeof(EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Partner.CreatePartnerOrderRequest)
            .Assembly.GetTypes()
            .Where(type => type.Namespace == "EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Partner"
                && type.Name.EndsWith("Request", StringComparison.Ordinal))
            .SelectMany(type => type.GetProperties()
                .Where(property => property.Name.Contains("PartnerId", StringComparison.OrdinalIgnoreCase))
                .Select(property => $"{type.Name}.{property.Name}"))
            .Should().BeEmpty();

    /// <summary>
    /// A shop request may name one amount, and only one: the total the customer was shown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prices come from Pricing. A shop request shape that could carry one would let the browser
    /// set it, and the bug would look like ordinary parameter passing in review — a field named
    /// <c>unitPrice</c> on an add-to-cart request reads as helpful until someone sends a cent.
    /// </para>
    /// <para>
    /// <c>ExpectedTotal</c> is the exception because it cannot cause a price. It is a claim about
    /// what was displayed, compared against a live quote, and its only possible effect is a
    /// refusal.
    /// </para>
    /// </remarks>
    [Fact]
    public void Shop_Request_Dtos_Accept_No_Money_Except_ExpectedTotal()
    {
        string[] moneyWords = ["Price", "Amount", "Total", "Money"];

        var carriers = new[] { "MoneyRequest", "MoneyGraph" };

        var shapes = ShopRequestShapes().ToList();

        // No money-shaped field anywhere, except the one claim about what was displayed.
        shapes
            .Where(type => !carriers.Contains(type.Name, StringComparer.Ordinal))
            .SelectMany(type => type.GetProperties()
                .Where(property => moneyWords.Any(word =>
                    property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Where(property => property.Name != "ExpectedTotal")
                .Select(property => $"{type.Name}.{property.Name}"))
            .Should().BeEmpty();

        // And the money carriers themselves are reachable only through that one field, so the
        // exemption above cannot be used to smuggle a second amount in under another name.
        shapes
            .SelectMany(type => type.GetProperties()
                .Where(property => carriers.Contains(
                    property.PropertyType.Name, StringComparer.Ordinal))
                .Where(property => property.Name != "ExpectedTotal")
                .Select(property => $"{type.Name}.{property.Name}"))
            .Should().BeEmpty();
    }

    private static IEnumerable<Type> ShopRequestShapes() =>
        typeof(EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Shop.AddCartLineRequest).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Shop"
                && type.Name.EndsWith("Request", StringComparison.Ordinal))
        .Concat(typeof(EwaysVNext.Modules.Sales.Adapter.Inbound.GraphQL.Shop.AddToCartInput).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "EwaysVNext.Modules.Sales.Adapter.Inbound.GraphQL.Shop"
                && (type.Name.EndsWith("Input", StringComparison.Ordinal)
                    || type.Name == "MoneyGraph")));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string SourceRoot() => Path.Combine(HexagonalBoundaryTests.Root, "src");
}
