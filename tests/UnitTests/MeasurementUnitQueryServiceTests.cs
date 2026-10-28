using AwesomeAssertions;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Services;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class MeasurementUnitQueryServiceTests
{
    [Fact]
    public async Task List_Returns_Repository_Snapshots()
    {
        var repository = Substitute.For<IMeasurementUnitRepository>();
        IReadOnlyList<MeasurementUnitSnapshot> snapshots =
        [
            new(1, "Kilogram", "kg"),
            new(2, "Inch", "in")
        ];
        repository.ListSnapshotsAsync(Arg.Any<CancellationToken>()).Returns(snapshots);
        var service = new MeasurementUnitQueryService(repository);

        var result = await service.ListAsync(TestContext.Current.CancellationToken);

        result.Should().BeSameAs(snapshots);
        await repository.Received(1).ListSnapshotsAsync(TestContext.Current.CancellationToken);
    }
}
