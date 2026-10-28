using System.Reflection;
using AwesomeAssertions;
using EwaysVNext.Modules.Catalog;
using EwaysVNext.Modules.Catalog.Application.Products.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

/// <summary>
/// PRD §2.1: the MVP has no stock and no reservations — every active variant is always available —
/// so the Product aggregate must carry no stock state and Catalog must register no background
/// reaper.
/// </summary>
public sealed class ProductShapeTests
{
    private static readonly Assembly CatalogOutboundAdapter =
        typeof(EwaysVNext.Modules.Catalog.Adapter.Outbound.DependencyInjection).Assembly;

    [Fact]
    public void Product_Exposes_No_Stock_Or_Reservations() =>
        typeof(Product).GetProperties().Select(p => p.Name).Should()
            .NotContain(["Stock", "ReservedStock", "AvailableStock", "Reservations"]);

    [Fact]
    public void Catalog_Registers_No_Hosted_Service_Of_Its_Own()
    {
        var services = new ServiceCollection().AddCatalogModule();

        // Catalog's own hosted services only: AddCatalogGraphQL brings in HotChocolate's
        // RequestExecutorWarmupService, which is not ours to assert away.
        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType)
            .Should().NotContain(t => t != null && t.Assembly == CatalogOutboundAdapter);
    }
}
