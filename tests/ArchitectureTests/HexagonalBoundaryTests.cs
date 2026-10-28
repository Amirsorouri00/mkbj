using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

/// <summary>
/// Executable module-boundary rules. Later phases append to <see cref="ModuleNames"/> and to the
/// sanctioned-reference theory as they create each module; a module missing from
/// <see cref="ModuleNames"/> escapes every rule here with the build still green, so adding the
/// name is part of adding the module.
/// </summary>
public sealed class HexagonalBoundaryTests
{
    // Final set (later phases append as they create each module):
    // Catalog, Customer, Partners, Settings, Pricing, Sales, Sourcing, Admin.
    public static readonly string[] ModuleNames = ["Catalog", "Customer"];

    [Fact]
    public void Src_Modules_Are_Exactly_ModuleNames() =>
        Directory.EnumerateDirectories(Path.Combine(Root, "src", "Modules"))
            .Select(Path.GetFileName).Should().BeEquivalentTo(ModuleNames);

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
