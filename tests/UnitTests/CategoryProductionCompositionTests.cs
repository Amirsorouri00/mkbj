using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class CategoryProductionCompositionTests
{
    [Fact]
    public void Host_and_module_use_the_single_unified_database()
    {
        var root = FindSolutionDirectory();
        var program = File.ReadAllText(Path.Combine(
            root, "src", "Host", "EwaysVNext.Host", "Program.cs"));
        var outbound = File.ReadAllText(Path.Combine(root, "src", "Modules", "Catalog",
            "Adapter", "Outbound", "DependencyInjection.cs"));

        program.Should().Contain("AddCatalogModule");
        program.Should().Contain("AddUnifiedDatabase");
        outbound.Should().NotContain("AddModuleDbContext");
    }

    [Fact]
    public void Host_composes_Category_through_the_Catalog_module()
    {
        var root = FindSolutionDirectory();
        var program = File.ReadAllText(Path.Combine(
            root, "src", "Host", "EwaysVNext.Host", "Program.cs"));

        program.Should().Contain("AddCatalogModule");
        program.Should().NotContain("AddCategoryModule");
    }

    private static string FindSolutionDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EwaysVNext.slnx")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the solution directory.");
    }
}
