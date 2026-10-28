using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Products;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Products.Services;
using EwaysVNext.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class CategoryProductionCompositionTests
{
    [Fact]
    public void Catalog_interfaces_resolve_to_the_same_scoped_ProductService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IProductRepository>());
        services.AddSingleton(Substitute.For<IUnitOfWork>());
        services.AddSingleton(Substitute.For<IIdGenerator>());
        services.AddProductApplication();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var internalService = scope.ServiceProvider.GetRequiredService<IProductService>();
        var contractType = typeof(ProductService).Assembly
            .GetReferencedAssemblies()
            .Select(assemblyName => System.Reflection.Assembly.Load(assemblyName))
            .Select(assembly => assembly.GetType(
                "EwaysVNext.Modules.Catalog.Contracts.Products.IProductAvailabilityContract"))
            .SingleOrDefault(type => type is not null);

        contractType.Should().NotBeNull();
        var publicService = scope.ServiceProvider.GetRequiredService(contractType!);
        publicService.Should().BeSameAs(internalService);
    }

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
