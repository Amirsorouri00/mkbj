using AwesomeAssertions;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using EwaysVNext.Outcomes;
using EwaysVNext.Presentation.Endpoint.GraphQL.Exceptions;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class AttributeBlueprintArchiveMutationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mutation_Forwards_Id_And_Cancellation_To_Service(bool archive)
    {
        var service = Substitute.For<IAttributeBlueprintService>();
        service.ArchiveAsync(42, Arg.Any<CancellationToken>()).Returns(Result.Success());
        service.UnarchiveAsync(42, Arg.Any<CancellationToken>()).Returns(Result.Success());
        var mutations = new AttributeBlueprintMutations();

        var result = await mutations.SetAttributeBlueprintArchiveStatus(42, archive, service, TestContext.Current.CancellationToken);

        result.Should().BeTrue();
        if (archive)
            await service.Received(1).ArchiveAsync(42, TestContext.Current.CancellationToken);
        else
            await service.Received(1).UnarchiveAsync(42, TestContext.Current.CancellationToken);
        service.ReceivedCalls().Should().ContainSingle();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mutation_Propagates_Service_Failure_As_GraphQL_Error(bool archive)
    {
        var service = Substitute.For<IAttributeBlueprintService>();
        var failure = Result.Failure(new Error("ATTRIBUTE_BLUEPRINT_NOT_FOUND", "Attribute blueprint was not found."));
        service.ArchiveAsync(42, Arg.Any<CancellationToken>()).Returns(failure);
        service.UnarchiveAsync(42, Arg.Any<CancellationToken>()).Returns(failure);
        var mutations = new AttributeBlueprintMutations();

        var exception = await Assert.ThrowsAsync<NotFoundGraphQLException>(() =>
            mutations.SetAttributeBlueprintArchiveStatus(42, archive, service, TestContext.Current.CancellationToken));

        exception.Code.Should().Be("ATTRIBUTE_BLUEPRINT_NOT_FOUND");
    }
}
