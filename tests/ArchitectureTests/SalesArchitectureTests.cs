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

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string SourceRoot() => Path.Combine(HexagonalBoundaryTests.Root, "src");
}
