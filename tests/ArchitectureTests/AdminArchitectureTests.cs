using System.Reflection;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

/// <summary>
/// Admin composes; it does not own.
/// </summary>
/// <remarks>
/// A composition module with a table of its own stops being a composition module. It becomes a
/// second place where an order's status lives, and the two drift — which is the whole failure
/// ADR-0006 exists to prevent. These rules are what keep the "data-less" in "data-less
/// composition module" from being a comment.
/// </remarks>
public sealed class AdminArchitectureTests
{
    [Fact]
    public void Admin_Composition_Layers_Must_Be_Aggregate_Free()
    {
        var result = Types.InAssemblies(
            [
                Load("EwaysVNext.Modules.Admin.Application"),
                Load("EwaysVNext.Modules.Admin.Adapter.Inbound.Rest"),
                Load("EwaysVNext.Modules.Admin.Adapter.Inbound.GraphQL"),
            ])
            .ShouldNot()
            .HaveDependencyOnAny(
                "EwaysVNext.Domain.Common.AggregateRoot",
                "EwaysVNext.Domain.Common.Entity",
                "EwaysVNext.Domain.Common.ValueObject",
                "Microsoft.EntityFrameworkCore",
                "EwaysVNext.Persistence",
                "EwaysVNext.Database")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "offending: {0}", string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Admin_Owns_No_DbContext_And_No_Read_Side()
    {
        Directory.Exists(Path.Combine(HexagonalBoundaryTests.Root, "src/Modules/Admin/QueryApplication"))
            .Should().BeFalse("Admin composes other modules' read sides rather than having one");

        Directory.Exists(Path.Combine(HexagonalBoundaryTests.Root, "src/Database/Admin"))
            .Should().BeFalse("a composition module with tables is not one");

        AdminAssemblies()
            .SelectMany(assembly => assembly.GetTypes())
            .Should().NotContain(type => typeof(DbContext).IsAssignableFrom(type));
    }

    /// <summary>
    /// Admin's hexagon references no other module at all.
    /// </summary>
    /// <remarks>
    /// The gateways may name another module's read types — that is their job — but the
    /// Application must not, or the composition would be structural rather than a port, and
    /// every provider rename would reach the services.
    /// </remarks>
    [Fact]
    public void Admin_Application_References_No_Other_Module() =>
        ModuleReferences("EwaysVNext.Modules.Admin.Application").Should().BeEmpty();

    /// <summary>
    /// The composition layer reads read sides, not write services.
    /// </summary>
    /// <remarks>
    /// One Application assembly is allowed through — Customer's, because its read DTO embeds a
    /// contract record that lives there. This asserts what that exception does <b>not</b> buy:
    /// no inbound port, no service and no aggregate from any other module is used.
    /// </remarks>
    [Fact]
    public void Admin_Uses_No_Other_Modules_Services()
    {
        var offenders = Load("EwaysVNext.Modules.Admin.Adapter.Outbound")
            .GetTypes()
            .SelectMany(type => type.GetConstructors())
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName ?? string.Empty)
            .Where(name => name.StartsWith("EwaysVNext.Modules.", StringComparison.Ordinal))
            .Where(name => !name.StartsWith("EwaysVNext.Modules.Admin.", StringComparison.Ordinal))
            .Where(name => !name.Contains(".QueryApplication.", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty(
            "a gateway takes another module's read side, never its services");
    }

    private static IEnumerable<Assembly> AdminAssemblies() =>
        Directory.EnumerateFiles(AppContext.BaseDirectory, "EwaysVNext.Modules.Admin.*.dll")
            .Select(Assembly.LoadFrom);

    private static List<string> ModuleReferences(string assemblyName) =>
        [.. Load(assemblyName).GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("EwaysVNext.Modules.", StringComparison.Ordinal))
            .Where(name => !name.StartsWith("EwaysVNext.Modules.Admin.", StringComparison.Ordinal))];

    private static Assembly Load(string name) =>
        Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, $"{name}.dll"));
}
