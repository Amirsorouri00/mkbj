using AwesomeAssertions;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Outcomes;
using NSubstitute;
using ApplicationOptionInput = EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound.AttributeBlueprintOptionInput;
using ApplicationUpdateOptionInput = EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound.UpdateAttributeBlueprintOptionInput;
using IAttributeBlueprintService = EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound.IAttributeBlueprintService;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class AttributeBlueprintGraphQlTests
{
    [Fact]
    public async Task Create_Forwards_Options_To_The_Catalog_Service()
    {
        var blueprints = Substitute.For<IAttributeBlueprintService>();
        blueprints.CreateAsync("Weight", AttributeBlueprintDataType.Number,
                Arg.Any<IReadOnlyCollection<ApplicationOptionInput>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(42L));
        var input = new CreateAttributeBlueprintInput("Weight", AttributeBlueprintDataType.Number,
            [new AttributeBlueprintOptionInput("5", 7), new AttributeBlueprintOptionInput("10", 7)]);

        var id = await new AttributeBlueprintMutations().CreateAttributeBlueprint(input, blueprints, TestContext.Current.CancellationToken);

        id.Should().Be(42);
        await blueprints.Received(1).CreateAsync("Weight", AttributeBlueprintDataType.Number,
            Arg.Is<IReadOnlyCollection<ApplicationOptionInput>>(values =>
                values.Count == 2 && values.First().MeasurementUnitId == 7),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Option_Mutations_Forward_All_Arguments()
    {
        var blueprints = Substitute.For<IAttributeBlueprintService>();
        blueprints.AddOptionAsync(42, "5", 7, Arg.Any<CancellationToken>()).Returns(Result.Success(101L));
        blueprints.UpdateOptionAsync(42, 101, "10", 7, Arg.Any<CancellationToken>()).Returns(Result.Success());
        blueprints.RemoveOptionAsync(42, 101, Arg.Any<CancellationToken>()).Returns(Result.Success());
        var mutations = new AttributeBlueprintMutations();

        var optionId = await mutations.AddAttributeBlueprintOption(
            new AddAttributeBlueprintOptionInput(42, "5", 7), blueprints, TestContext.Current.CancellationToken);
        var updated = await mutations.UpdateAttributeBlueprintOption(
            new UpdateAttributeBlueprintOptionInput(42, 101, "10", 7), blueprints, TestContext.Current.CancellationToken);
        var removed = await mutations.RemoveAttributeBlueprintOption(42, 101, blueprints, TestContext.Current.CancellationToken);

        optionId.Should().Be(101);
        updated.Should().BeTrue();
        removed.Should().BeTrue();
        await blueprints.Received(1).AddOptionAsync(42, "5", 7, TestContext.Current.CancellationToken);
        await blueprints.Received(1).UpdateOptionAsync(42, 101, "10", 7, TestContext.Current.CancellationToken);
        await blueprints.Received(1).RemoveOptionAsync(42, 101, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Update_Forwards_The_Complete_Editable_State()
    {
        var blueprints = Substitute.For<IAttributeBlueprintService>();
        blueprints.UpdateAsync(42, "Material", AttributeBlueprintDataType.Text, false,
                Arg.Any<IReadOnlyCollection<ApplicationUpdateOptionInput>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var input = new UpdateAttributeBlueprintInput(42, "Material", AttributeBlueprintDataType.Text, false,
            [new AttributeBlueprintOptionUpdateInput(101, "Cotton", null),
                new AttributeBlueprintOptionUpdateInput(null, "Silk", null)]);

        var updated = await new AttributeBlueprintMutations().UpdateAttributeBlueprint(input, blueprints, TestContext.Current.CancellationToken);

        updated.Should().BeTrue();
        await blueprints.Received(1).UpdateAsync(42, "Material", AttributeBlueprintDataType.Text, false,
            Arg.Is<IReadOnlyCollection<ApplicationUpdateOptionInput>>(values =>
                values.Count == 2 && values.First().OptionId == 101 && values.Last().OptionId == null),
            TestContext.Current.CancellationToken);
    }
}
