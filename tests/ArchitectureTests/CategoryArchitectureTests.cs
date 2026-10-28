using System.Xml.Linq;
using AwesomeAssertions;
using Xunit;

namespace EwaysVNext.ArchitectureTests;

public sealed class CategoryArchitectureTests
{
    private const string CatalogContractProject =
        "EwaysVNext.Modules.Catalog.Contracts/EwaysVNext.Modules.Catalog.Contracts.csproj";

    [Fact]
    public void Catalog_contract_uses_the_canonical_plural_project_and_namespace()
    {
        var root = FindSolutionDirectory();
        var canonicalProject = Path.Combine(root,
            "EwaysVNext.Modules.Catalog.Contracts",
            "EwaysVNext.Modules.Catalog.Contracts.csproj");

        File.Exists(canonicalProject).Should().BeTrue();
        File.Exists(Path.Combine(root, "EwaysVNext.Modules.Catalog.Contract",
            "EwaysVNext.Modules.Catalog.Contracts.csproj"))
            .Should().BeFalse();
        XDocument.Load(canonicalProject).Descendants("RootNamespace")
            .Single().Value.Should().Be("EwaysVNext.Modules.Catalog.Contracts");
    }

    [Fact]
    public void Category_write_side_is_owned_by_the_Catalog_application()
    {
        var root = FindSolutionDirectory();

        File.Exists(Path.Combine(root, "src", "Modules", "Catalog", "Application",
            "Categories", "Domain", "Category.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "src", "Modules", "Category", "Application",
            "EwaysVNext.Modules.Category.Application.csproj"))
            .Should().BeFalse();
    }

    [Fact]
    public void Category_persistence_is_owned_by_the_Catalog_outbound_adapter()
    {
        var root = FindSolutionDirectory();

        File.Exists(Path.Combine(root, "src", "Modules", "Catalog", "Adapter", "Outbound",
            "Persistence", "Categories", "CategoryRepository.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "src", "Modules", "Category", "Adapter", "Outbound",
            "EwaysVNext.Modules.Category.Adapter.Outbound.csproj"))
            .Should().BeFalse();
    }

    [Fact]
    public void Category_read_side_is_owned_by_Catalog_without_an_application_cycle()
    {
        var root = FindSolutionDirectory();
        var catalogApplication = ReadProject(
            "src/Modules/Catalog/Application/EwaysVNext.Modules.Catalog.Application.csproj");
        var catalogQueries = ReadProject(
            "src/Modules/Catalog/QueryApplication/EwaysVNext.Modules.Catalog.QueryApplication.csproj");

        File.Exists(Path.Combine(root, "src", "Modules", "Catalog", "QueryApplication",
            "Categories", "CategoryQueries.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "src", "Modules", "Category", "QueryApplication",
            "EwaysVNext.Modules.Category.QueryApplication.csproj")).Should().BeFalse();
        catalogApplication.Descendants("ProjectReference")
            .Should().NotContain(reference =>
                reference.Attribute("Include")!.Value.Contains("QueryApplication"));
        catalogQueries.Descendants("ProjectReference")
            .Should().Contain(reference =>
                reference.Attribute("Include")!.Value.Contains("Database"));
    }

    [Fact]
    public void Category_rest_adapter_is_owned_by_Catalog()
    {
        var root = FindSolutionDirectory();

        File.Exists(Path.Combine(root, "src", "Modules", "Catalog", "Adapter", "Inbound",
            "Rest", "Categories", "CategoryPublicEndpoints.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "src", "Modules", "Category", "Adapter", "Inbound",
            "Rest", "EwaysVNext.Modules.Category.Adapter.Inbound.Rest.csproj"))
            .Should().BeFalse();
    }

    [Fact]
    public void Category_graphql_and_composition_are_owned_by_Catalog()
    {
        var root = FindSolutionDirectory();
        var catalogInstaller = ReadProject(
            "src/Modules/Catalog/Installer/EwaysVNext.Modules.Catalog.Installer.csproj");
        var references = catalogInstaller.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")!.Value.Replace('\\', '/'));

        File.Exists(Path.Combine(root, "src", "Modules", "Catalog", "Adapter", "Inbound",
            "GraphQL", "Categories", "CategoryAdminQueries.cs")).Should().BeTrue();
        references.Should().Contain(reference =>
            reference.EndsWith("Catalog.Adapter.Inbound.GraphQL.csproj"));
        var formerCategoryModule = Path.Combine(root, "src", "Modules", "Category");
        var remainingProjects = Directory.Exists(formerCategoryModule)
            ? Directory.GetFiles(formerCategoryModule, "*.csproj", SearchOption.AllDirectories)
            : [];
        remainingProjects
            .Should().BeEmpty();
    }

    [Fact]
    public void GraphQl_server_and_endpoints_are_centralized_without_leaking_Catalog_resolvers()
    {
        var root = FindSolutionDirectory();
        var sources = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .ToArray();

        var serverRegistrations = sources
            .Where(source => source.Text.Contains("AddGraphQLServer(", StringComparison.Ordinal))
            .ToArray();
        var graphQlBootstrapPath = Path.Combine("BuildingBlocks", "Presentation.Endpoint", "GraphQL");
        serverRegistrations.Should().ContainSingle();
        serverRegistrations.Single().Path.Should().Contain(
            graphQlBootstrapPath);

        var endpointMappings = sources
            .Where(source => source.Text.Contains("MapGraphQL(", StringComparison.Ordinal)
                || source.Text.Contains("MapNitroApp(", StringComparison.Ordinal))
            .ToArray();
        endpointMappings.Should().OnlyContain(source => source.Path.Contains(
            graphQlBootstrapPath,
            StringComparison.OrdinalIgnoreCase));

        sources.Should().NotContain(source => source.Text.Contains(
            "/api/v1/admin/category/graphql", StringComparison.Ordinal));

        var hostSource = File.ReadAllText(Path.Combine(root, "src", "Host", "EwaysVNext.Host", "Program.cs"));
        hostSource.Should().NotContain("CategoryAdminQueries");
        hostSource.Should().NotContain("CategoryAdminMutations");

        var hostProject = ReadProject("src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj");
        hostProject.Descendants("ProjectReference").Should().NotContain(reference =>
            reference.Attribute("Include")!.Value.Replace('\\', '/').EndsWith(
                "Catalog.Adapter.Inbound.GraphQL.csproj", StringComparison.Ordinal));

        File.Exists(Path.Combine(root, "src", "Modules", "Catalog", "Adapter", "Inbound",
            "GraphQL", "Categories", "CategoryAdminQueries.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "src", "Modules", "Catalog", "Adapter", "Inbound",
            "GraphQL", "Categories", "CategoryAdminMutations.cs")).Should().BeTrue();
    }

    [Fact]
    public void Catalog_contract_is_framework_independent()
    {
        var project = ReadProject(CatalogContractProject);

        project.Descendants("ProjectReference").Should().OnlyContain(
            reference => reference.Attribute("Include")!.Value.EndsWith("EwaysVNext.Domain.csproj", StringComparison.Ordinal),
            "the Catalog contract may depend on the dependency-free Domain building block (for SnowflakeId), nothing else");
        project.Descendants("PackageReference").Should().BeEmpty();
        project.Descendants("FrameworkReference").Should().BeEmpty();
    }

    [Fact]
    public void Category_DTOs_are_owned_by_the_Catalog_contract()
    {
        var root = FindSolutionDirectory();
        var contract = ReadProject(CatalogContractProject);

        contract.Descendants("ProjectReference").Should().OnlyContain(
            reference => reference.Attribute("Include")!.Value.EndsWith("EwaysVNext.Domain.csproj", StringComparison.Ordinal),
            "the Catalog contract may depend on the dependency-free Domain building block (for SnowflakeId), nothing else");
        contract.Descendants("PackageReference").Should().BeEmpty();
        File.Exists(Path.Combine(root, "EwaysVNext.Modules.Catalog.Contracts",
            "Categories", "CategoryDtos.cs")).Should().BeTrue();
        File.Exists(Path.Combine(root, "EwaysVNext.Modules.Category.Contracts",
            "EwaysVNext.Modules.Category.Contracts.csproj"))
            .Should().BeFalse();
    }

    [Fact]
    public void Hierarchy_loading_has_cycle_safe_single_roundtrip_provider_paths()
    {
        var repository = File.ReadAllText(Path.Combine(
            FindSolutionDirectory(), "src/Modules/Catalog",
            "Adapter/Outbound/Persistence/Categories/CategoryRepository.cs"));

        repository.Should().Contain("Database.IsSqlServer()", because:
            "SQL Server must use its recursive hierarchy query while InMemory materializes once");
        repository.Should().Contain("WITH Hierarchy", because:
            "SQL Server hierarchy loading must be recursive");
        repository.Should().Contain("CHARINDEX", because:
            "the recursive SQL path must stop when it encounters a stored cycle");
        repository.Should().Contain("IgnoreQueryFilters()", because:
            "deleted intermediate categories remain part of the hierarchy topology");
    }

    private static XDocument ReadProject(string projectFile) =>
        XDocument.Load(Path.Combine(FindSolutionDirectory(), projectFile));

    private static string FindSolutionDirectory()
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
