using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Services;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Validation;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class AttributeBlueprintOptionServiceTests
{
    private const long BlueprintId = 42;
    private const long OptionId = 101;
    private readonly IAttributeBlueprintRepository _repository = Substitute.For<IAttributeBlueprintRepository>();
    private readonly IMeasurementUnitRepository _measurementUnitRepository = Substitute.For<IMeasurementUnitRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IAttributeBlueprintDependencyGateway _dependencyGateway = Substitute.For<IAttributeBlueprintDependencyGateway>();
    private readonly IIdGenerator _idGenerator = Substitute.For<IIdGenerator>();
    private readonly FixedClock _clock = new();

    [Fact]
    public async Task Create_With_Initial_Options_Builds_One_Aggregate_And_Saves_Once()
    {
        _idGenerator.Generate().Returns(BlueprintId, OptionId, OptionId + 1);
        _repository.NameExistsAsync("Weight", null, Arg.Any<CancellationToken>()).Returns(false);
        _measurementUnitRepository.ExistsAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        var options = new[]
        {
            new AttributeBlueprintOptionInput(" 5.00 ", 7),
            new AttributeBlueprintOptionInput("10", 7)
        };

        var result = await CreateService().CreateAsync(
            " Weight ", AttributeBlueprintDataType.Number, options, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(BlueprintId);
        await _repository.Received(1).AddAsync(
            Arg.Is<AttributeBlueprint>(blueprint =>
                blueprint.Name == "Weight" &&
                blueprint.Options.Count == 2 &&
                blueprint.Options.ElementAt(0).Value == "5" &&
                blueprint.Options.ElementAt(1).Value == "10" &&
                blueprint.Options.All(option => option.MeasurementUnitId == 7)),
            TestContext.Current.CancellationToken);
        await _measurementUnitRepository.Received(1).ExistsAsync(7, TestContext.Current.CancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Create_With_Invalid_Option_Does_Not_Create_Or_Query_Dependencies()
    {
        _idGenerator.Generate().Returns(BlueprintId, OptionId);
        var options = new[] { new AttributeBlueprintOptionInput("not-a-number", 7) };

        var result = await CreateService().CreateAsync(
            "Weight", AttributeBlueprintDataType.Number, options, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_NUMBER_INVALID");
        _repository.ReceivedCalls().Should().BeEmpty();
        _measurementUnitRepository.ReceivedCalls().Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_With_Null_Options_Returns_Validation_Without_Using_Dependencies()
    {
        var result = await CreateService().CreateAsync(
            "Weight", AttributeBlueprintDataType.Number, null!, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        _repository.ReceivedCalls().Should().BeEmpty();
        _measurementUnitRepository.ReceivedCalls().Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Create_With_Missing_Unit_Does_Not_Persist_Aggregate()
    {
        _idGenerator.Generate().Returns(BlueprintId, OptionId);
        _repository.NameExistsAsync("Weight", null, Arg.Any<CancellationToken>()).Returns(false);
        _measurementUnitRepository.ExistsAsync(7, Arg.Any<CancellationToken>()).Returns(false);
        var options = new[] { new AttributeBlueprintOptionInput("5", 7) };

        var result = await CreateService().CreateAsync(
            "Weight", AttributeBlueprintDataType.Number, options, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("MEASUREMENT_UNIT_NOT_FOUND");
        await _repository.DidNotReceive().AddAsync(Arg.Any<AttributeBlueprint>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Add_Number_Option_Verifies_Unit_Then_Saves_And_Returns_Option_Id()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Number);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _measurementUnitRepository.ExistsAsync(7, Arg.Any<CancellationToken>()).Returns(true);
        _idGenerator.Generate().Returns(OptionId);

        var result = await CreateService().AddOptionAsync(
            BlueprintId, " 5.00 ", 7, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(OptionId);
        var option = blueprint.Options.Should().ContainSingle().Subject;
        option.Value.Should().Be("5");
        option.MeasurementUnitId.Should().Be(7);
        await _measurementUnitRepository.Received(1).ExistsAsync(7, TestContext.Current.CancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Add_With_Missing_Unit_Does_Not_Generate_Or_Mutate()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Number);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _measurementUnitRepository.ExistsAsync(7, Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateService().AddOptionAsync(
            BlueprintId, "5", 7, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("MEASUREMENT_UNIT_NOT_FOUND");
        blueprint.Options.Should().BeEmpty();
        _idGenerator.ReceivedCalls().Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Non_Number_Unit_Is_Rejected_By_Domain_Without_Unit_Query()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Text);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _idGenerator.Generate().Returns(OptionId);

        var result = await CreateService().AddOptionAsync(
            BlueprintId, "Cotton", 7, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_UNIT_NOT_ALLOWED");
        _measurementUnitRepository.ReceivedCalls().Should().BeEmpty();
        blueprint.Options.Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Update_Normalized_NoOp_Succeeds_And_Saves_Once()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Number);
        blueprint.AddOption(OptionId, "5", 7, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _measurementUnitRepository.ExistsAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().UpdateOptionAsync(
            BlueprintId, OptionId, "05.00", 7, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.Options.Single().Value.Should().Be("5");
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Update_Unknown_Option_Returns_NotFound_Without_Unit_Query()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Number);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);

        var result = await CreateService().UpdateOptionAsync(
            BlueprintId, OptionId, "5", 7, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_NOT_FOUND");
        _measurementUnitRepository.ReceivedCalls().Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Update_Reconciles_All_Editable_Fields_And_Options()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Text);
        blueprint.AddOption(OptionId, "Cotton", null, _clock.UtcNow);
        blueprint.AddOption(OptionId + 1, "Wool", null, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _repository.NameExistsAsync("Material", BlueprintId, Arg.Any<CancellationToken>()).Returns(false);
        _dependencyGateway.CountProductValuesForOptionAsync(BlueprintId, OptionId, Arg.Any<CancellationToken>()).Returns(2);
        _idGenerator.Generate().Returns(OptionId + 2);
        var options = new[]
        {
            new UpdateAttributeBlueprintOptionInput(OptionId + 1, "Merino", null),
            new UpdateAttributeBlueprintOptionInput(null, "Silk", null)
        };

        var result = await CreateService().UpdateAsync(
            BlueprintId, " Material ", AttributeBlueprintDataType.Text, false, options,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.Name.Should().Be("Material");
        blueprint.Status.Should().Be(AttributeBlueprintStatus.Inactive);
        blueprint.Options.Select(option => option.Id).Should().BeEquivalentTo([OptionId + 1, OptionId + 2]);
        blueprint.Options.Single(option => option.Id == OptionId + 1).Value.Should().Be("Merino");
        blueprint.Options.Single(option => option.Id == OptionId + 2).Value.Should().Be("Silk");
        await _dependencyGateway.Received(1).RemoveProductValuesForOptionAsync(
            BlueprintId, OptionId, TestContext.Current.CancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Update_Allows_Swapping_Existing_Option_Values()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Text);
        blueprint.AddOption(OptionId, "Red", null, _clock.UtcNow);
        blueprint.AddOption(OptionId + 1, "Blue", null, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);

        var result = await CreateService().UpdateAsync(BlueprintId, "Attribute", AttributeBlueprintDataType.Text, true,
            [new(OptionId, "Blue", null), new(OptionId + 1, "Red", null)], TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.Options.Single(option => option.Id == OptionId).Value.Should().Be("Blue");
        blueprint.Options.Single(option => option.Id == OptionId + 1).Value.Should().Be("Red");
        _dependencyGateway.ReceivedCalls().Should().BeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Invalid_Update_Does_Not_Change_Tracked_Blueprint()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Text);
        blueprint.AddOption(OptionId, "Red", null, _clock.UtcNow);
        blueprint.AddOption(OptionId + 1, "Blue", null, _clock.UtcNow);
        blueprint.AddOption(OptionId + 2, "Green", null, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);

        var result = await CreateService().UpdateAsync(BlueprintId, "Changed", AttributeBlueprintDataType.Text, false,
            [new(OptionId, "Blue", null), new(OptionId + 1, " blue ", null)], TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_OPTION_VALUE_TAKEN");
        blueprint.Name.Should().Be("Attribute");
        blueprint.Status.Should().Be(AttributeBlueprintStatus.Active);
        blueprint.Options.Select(option => option.Value).Should().Equal("Red", "Blue", "Green");
        blueprint.DomainEvents.Should().BeEmpty();
        _dependencyGateway.ReceivedCalls().Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Unavailable_Dependency_Check_Does_Not_Change_Tracked_Blueprint()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Text);
        blueprint.AddOption(OptionId, "Red", null, _clock.UtcNow);
        blueprint.AddOption(OptionId + 1, "Blue", null, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _dependencyGateway.CountProductValuesForOptionAsync(BlueprintId, OptionId + 1, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new NotImplementedException()));

        await Assert.ThrowsAsync<NotImplementedException>(() => CreateService().UpdateAsync(
            BlueprintId, "Changed", AttributeBlueprintDataType.Text, false,
            [new(OptionId, "Green", null)], TestContext.Current.CancellationToken));

        blueprint.Name.Should().Be("Attribute");
        blueprint.Status.Should().Be(AttributeBlueprintStatus.Active);
        blueprint.Options.Select(option => option.Value).Should().Equal("Red", "Blue");
        blueprint.DomainEvents.Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Update_DataType_Reuses_Options_Against_The_New_Type()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Text);
        blueprint.AddOption(OptionId, "Five", null, _clock.UtcNow);
        var originalOption = blueprint.Options.Single();
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _dependencyGateway.GetDependenciesAsync(BlueprintId, Arg.Any<CancellationToken>())
            .Returns(new AttributeBlueprintDependencies(0, 0));
        _measurementUnitRepository.ExistsAsync(7, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().UpdateAsync(
            BlueprintId,
            "Attribute",
            AttributeBlueprintDataType.Number,
            true,
            [new UpdateAttributeBlueprintOptionInput(OptionId, "5.00", 7)],
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.DataType.Should().Be(AttributeBlueprintDataType.Number);
        var option = blueprint.Options.Should().ContainSingle().Subject;
        option.Should().BeSameAs(originalOption);
        option.Id.Should().Be(OptionId);
        option.Value.Should().Be("5");
        option.MeasurementUnitId.Should().Be(7);
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Remove_InUse_Option_Removes_Product_Values_And_Saves()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Text);
        blueprint.AddOption(OptionId, "Cotton", null, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _dependencyGateway.CountProductValuesForOptionAsync(BlueprintId, OptionId, Arg.Any<CancellationToken>()).Returns(3);

        var result = await CreateService().RemoveOptionAsync(
            BlueprintId, OptionId, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.Options.Should().BeEmpty();
        await _dependencyGateway.Received(1).RemoveProductValuesForOptionAsync(
            BlueprintId, OptionId, TestContext.Current.CancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Remove_Unused_Option_Does_Not_Call_Product_Removal()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Text);
        blueprint.AddOption(OptionId, "Cotton", null, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _dependencyGateway.CountProductValuesForOptionAsync(BlueprintId, OptionId, Arg.Any<CancellationToken>()).Returns(0);

        var result = await CreateService().RemoveOptionAsync(
            BlueprintId, OptionId, ct: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.Options.Should().BeEmpty();
        await _dependencyGateway.DidNotReceive().RemoveProductValuesForOptionAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Change_DataType_With_Options_Is_Blocked_Before_Product_Dependency_Query()
    {
        var blueprint = CreateBlueprint(AttributeBlueprintDataType.Text);
        blueprint.AddOption(OptionId, "Cotton", null, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);

        var result = await CreateService().ChangeDataTypeAsync(
            BlueprintId, AttributeBlueprintDataType.Number, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_DATA_TYPE_HAS_OPTIONS");
        _dependencyGateway.ReceivedCalls().Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public async Task Update_And_Remove_Reject_Invalid_Ids_Before_Loading(long blueprintId, long optionId)
    {
        var service = CreateService();

        var update = await service.UpdateOptionAsync(
            blueprintId, optionId, "Cotton", null, TestContext.Current.CancellationToken);
        var remove = await service.RemoveOptionAsync(
            blueprintId, optionId, ct: TestContext.Current.CancellationToken);

        update.IsFailure.Should().BeTrue();
        update.Error!.Code.Should().Be("VALIDATION_FAILED");
        remove.IsFailure.Should().BeTrue();
        remove.Error!.Code.Should().Be("VALIDATION_FAILED");
        _repository.ReceivedCalls().Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    private AttributeBlueprint CreateBlueprint(AttributeBlueprintDataType dataType)
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Attribute", dataType, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        return blueprint;
    }

    private AttributeBlueprintService CreateService()
        => new(
            _repository,
            _measurementUnitRepository,
            _unitOfWork,
            _dependencyGateway,
            _clock,
            _idGenerator,
            new CreateAttributeBlueprintValidator(),
            new RenameAttributeBlueprintValidator(),
            new ChangeAttributeBlueprintDataTypeValidator(),
            new AttributeBlueprintIdValidator(),
            new AttributeBlueprintOptionInputValidator(),
            new AttributeBlueprintOptionIdsValidator(),
            new UpdateAttributeBlueprintValidator());
}
