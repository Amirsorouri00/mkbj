using AwesomeAssertions;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Services;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using EwaysVNext.Queries.Application.Paginations;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class AttributeBlueprintQueryServiceTests
{
    private readonly IAttributeBlueprintRepository _repository = Substitute.For<IAttributeBlueprintRepository>();
    private readonly IAttributeBlueprintDependencyGateway _dependencyGateway = Substitute.For<IAttributeBlueprintDependencyGateway>();

    [Fact]
    public async Task GetById_When_Blueprint_Does_Not_Exist_Returns_NotFound()
    {
        _repository.GetSnapshotByIdAsync(42, Arg.Any<CancellationToken>()).Returns((AttributeBlueprintSnapshot?)null);
        var service = new AttributeBlueprintQueryService(_repository, _dependencyGateway);

        var result = await service.GetByIdAsync(42, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_NOT_FOUND");
    }

    [Fact]
    public async Task List_Forwards_Paging_And_Filters_To_Repository()
    {
        var snapshots = new PaginatedQuery<AttributeBlueprintSnapshot>(2, 25, 1, 5, []);
        var filter = new AttributeBlueprintFilter("color", AttributeBlueprintStatusFilter.Active, AttributeBlueprintDataTypeFilter.Text);
        _repository.ListSnapshotsAsync(2, 25, true, filter, Arg.Any<CancellationToken>()).Returns(snapshots);
        var service = new AttributeBlueprintQueryService(_repository, _dependencyGateway);

        var result = await service.ListAsync(2, 25, true, filter, TestContext.Current.CancellationToken);

        result.Should().BeSameAs(snapshots);
        await _repository.Received(1).ListSnapshotsAsync(2, 25, true, filter, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetUsage_Returns_Dependency_Counts()
    {
        var snapshot = new AttributeBlueprintSnapshot(42, "Color", "color", "Text", "Active", "Global", null, [], null, null, DateTimeOffset.UtcNow, null, []);
        _repository.GetSnapshotByIdAsync(42, Arg.Any<CancellationToken>()).Returns(snapshot);
        _dependencyGateway.GetDependenciesAsync(42, Arg.Any<CancellationToken>()).Returns(new AttributeBlueprintDependencies(3, 2));
        var service = new AttributeBlueprintQueryService(_repository, _dependencyGateway);

        var result = await service.GetUsageAsync(42, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProductValueCount.Should().Be(3);
        result.Value.CategoryMappingCount.Should().Be(2);
        result.Value.CanChangeDataType.Should().BeFalse();
        result.Value.CanDelete.Should().BeFalse();
    }
}
