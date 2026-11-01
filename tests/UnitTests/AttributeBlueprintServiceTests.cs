using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Services;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Validation;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class AttributeBlueprintServiceTests
{
    private const long BlueprintId = 42;

    private readonly IAttributeBlueprintRepository _repository = Substitute.For<IAttributeBlueprintRepository>();
    private readonly IMeasurementUnitRepository _measurementUnitRepository = Substitute.For<IMeasurementUnitRepository>();
    private readonly ICategoryRepository _categoryRepository = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IAttributeBlueprintDependencyGateway _dependencyGateway = Substitute.For<IAttributeBlueprintDependencyGateway>();
    private readonly IIdGenerator _idGenerator = Substitute.For<IIdGenerator>();
    private readonly FixedClock _clock = new();

    [Fact]
    public async Task Create_Normalizes_Name_Adds_Aggregate_And_Saves()
    {
        _idGenerator.Generate().Returns(BlueprintId);
        _repository.NameExistsAsync("Color", null, Arg.Any<CancellationToken>()).Returns(false);
        var service = CreateService();

        var result = await service.CreateAsync("  Color  ", AttributeBlueprintDataType.Text, [], TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(BlueprintId);
        await _repository.Received(1).AddAsync(
            Arg.Is<AttributeBlueprint>(blueprint =>
                blueprint.Id == BlueprintId &&
                blueprint.Name == "Color" &&
                blueprint.Status == AttributeBlueprintStatus.Active &&
                blueprint.DeletedAt == null),
            TestContext.Current.CancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Create_With_Duplicate_Name_Returns_Conflict_Without_Saving()
    {
        _idGenerator.Generate().Returns(BlueprintId);
        _repository.NameExistsAsync("Color", null, Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService();

        var result = await service.CreateAsync("Color", AttributeBlueprintDataType.Text, [], TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_NAME_TAKEN");
        await _repository.DidNotReceive().AddAsync(Arg.Any<AttributeBlueprint>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_With_Blank_Name_Returns_Validation_Without_Using_Repository()
    {
        var service = CreateService();

        var result = await service.CreateAsync("   ", AttributeBlueprintDataType.Text, [], TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        _idGenerator.DidNotReceive().Generate();
        await _repository.DidNotReceive().NameExistsAsync(
            Arg.Any<string>(),
            Arg.Any<long?>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rename_With_Duplicate_Name_Returns_Conflict_Without_Mutating()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _repository.NameExistsAsync("Size", BlueprintId, Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService();

        var result = await service.RenameAsync(BlueprintId, "Size", TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_NAME_TAKEN");
        blueprint.Name.Should().Be("Color");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("change-data-type")]
    [InlineData("activate")]
    [InlineData("deactivate")]
    [InlineData("archive")]
    [InlineData("unarchive")]
    public void Deleted_Blueprint_Rejects_Modifications_In_The_Aggregate(string operation)
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        blueprint.Delete(_clock.UtcNow);

        Action act = operation switch
        {
            "rename" => () => blueprint.Rename("Size", _clock.UtcNow),
            "change-data-type" => () => blueprint.ChangeDataType(AttributeBlueprintDataType.Number, _clock.UtcNow),
            "activate" => () => blueprint.Activate(_clock.UtcNow),
            "deactivate" => () => blueprint.Deactivate(_clock.UtcNow),
            "archive" => () => blueprint.Archive(_clock.UtcNow),
            "unarchive" => () => blueprint.Unarchive(_clock.UtcNow),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
        };

        act.Should().Throw<DomainException>()
            .Which.Code.Should().Be("ATTRIBUTE_BLUEPRINT_DELETED");
    }

    [Fact]
    public async Task Delete_With_NonPositive_Id_Returns_Validation_Without_Loading_Aggregate()
    {
        var service = CreateService();

        var result = await service.DeleteAsync(0, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        await _repository.DidNotReceive().GetByIdAsync(
            Arg.Any<long>(),
            Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangeDataType_With_ProductValues_Returns_InUse_Failure()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _dependencyGateway.GetDependenciesAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(new AttributeBlueprintDependencies(1, 0));
        var service = CreateService();

        var result = await service.ChangeDataTypeAsync(BlueprintId, AttributeBlueprintDataType.Number, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_DATA_TYPE_IN_USE");
        blueprint.DataType.Should().Be(AttributeBlueprintDataType.Text);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_With_ProductValues_Returns_InUse_Failure()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _dependencyGateway.GetDependenciesAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(new AttributeBlueprintDependencies(1, 2));
        var service = CreateService();

        var result = await service.DeleteAsync(BlueprintId, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_IN_USE_BY_PRODUCTS");
        blueprint.DeletedAt.Should().BeNull();
        await _dependencyGateway.DidNotReceive().RemoveCategoryMappingsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_With_CategoryMappings_Removes_Mappings_And_Deletes()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _dependencyGateway.GetDependenciesAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(new AttributeBlueprintDependencies(0, 2));
        var service = CreateService();

        var result = await service.DeleteAsync(BlueprintId, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.DeletedAt.Should().Be(_clock.UtcNow);
        blueprint.Status.Should().Be(AttributeBlueprintStatus.Inactive);
        await _dependencyGateway.Received(1).RemoveCategoryMappingsAsync(BlueprintId, TestContext.Current.CancellationToken);
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archive_Preserves_Blueprint(bool inactive)
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        if (inactive) blueprint.Deactivate(_clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = await CreateService().ArchiveAsync(BlueprintId, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.Status.Should().Be(AttributeBlueprintStatus.Archived);
        blueprint.PreviousStatus.Should().Be(inactive ? AttributeBlueprintStatus.Inactive : AttributeBlueprintStatus.Active);
        blueprint.DeletedAt.Should().BeNull();
        blueprint.Name.Should().Be("Color");
        blueprint.DataType.Should().Be(AttributeBlueprintDataType.Text);
        blueprint.UpdatedAt.Should().Be(_clock.UtcNow);
        blueprint.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<AttributeBlueprintArchivedDomainEvent>();
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
        await _dependencyGateway.Received(1).CountActiveCategoryAssignmentsAsync(BlueprintId, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deactivate_And_Archive_Are_Blocked_While_The_Attribute_Is_Still_Assigned_To_A_Category(bool archive)
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _dependencyGateway.CountActiveCategoryAssignmentsAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(1);
        var service = CreateService();

        var result = archive
            ? await service.ArchiveAsync(BlueprintId, TestContext.Current.CancellationToken)
            : await service.DeactivateAsync(BlueprintId, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_IN_USE_BY_CATEGORIES");
        blueprint.Status.Should().Be(AttributeBlueprintStatus.Active);
        blueprint.DomainEvents.Should().BeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Deactivate_And_Archive_Proceed_When_The_Attribute_Has_No_Active_Category_Assignments(bool archive)
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _dependencyGateway.CountActiveCategoryAssignmentsAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(0);
        var service = CreateService();

        var result = archive
            ? await service.ArchiveAsync(BlueprintId, TestContext.Current.CancellationToken)
            : await service.DeactivateAsync(BlueprintId, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.Status.Should().Be(archive ? AttributeBlueprintStatus.Archived : AttributeBlueprintStatus.Inactive);
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unarchive_Restores_Previous_Status_Without_Removing_Dependencies(bool inactive)
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        if (inactive) blueprint.Deactivate(_clock.UtcNow);
        blueprint.Archive(_clock.UtcNow);
        blueprint.ClearDomainEvents();
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _clock.Advance(TimeSpan.FromMinutes(1));

        var result = await CreateService().UnarchiveAsync(BlueprintId, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        var expectedStatus = inactive ? AttributeBlueprintStatus.Inactive : AttributeBlueprintStatus.Active;
        blueprint.Status.Should().Be(expectedStatus);
        blueprint.PreviousStatus.Should().BeNull();
        blueprint.DeletedAt.Should().BeNull();
        blueprint.Name.Should().Be("Color");
        blueprint.DataType.Should().Be(AttributeBlueprintDataType.Text);
        blueprint.UpdatedAt.Should().Be(_clock.UtcNow);
        blueprint.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<AttributeBlueprintUnarchivedDomainEvent>()
            .Which.Status.Should().Be(expectedStatus);
        await _unitOfWork.Received(1).SaveChangesAsync(TestContext.Current.CancellationToken);
        _dependencyGateway.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_Archive_And_Unarchive_Do_Not_Change_Status_Or_Emit_Extra_Events(bool inactive)
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        if (inactive) blueprint.Deactivate(_clock.UtcNow);
        var expectedStatus = blueprint.Status;
        blueprint.Archive(_clock.UtcNow);
        blueprint.ClearDomainEvents();
        var archivedAt = blueprint.UpdatedAt;

        blueprint.Archive(_clock.Advance(TimeSpan.FromMinutes(1)));

        blueprint.Status.Should().Be(AttributeBlueprintStatus.Archived);
        blueprint.PreviousStatus.Should().Be(expectedStatus);
        blueprint.UpdatedAt.Should().Be(archivedAt);
        blueprint.DomainEvents.Should().BeEmpty();

        blueprint.Unarchive(_clock.UtcNow);
        blueprint.ClearDomainEvents();
        var unarchivedAt = blueprint.UpdatedAt;
        blueprint.Unarchive(_clock.Advance(TimeSpan.FromMinutes(1)));

        blueprint.Status.Should().Be(expectedStatus);
        blueprint.PreviousStatus.Should().BeNull();
        blueprint.UpdatedAt.Should().Be(unarchivedAt);
        blueprint.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void New_Archive_Cycle_Saves_The_Current_Status()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        blueprint.Archive(_clock.UtcNow);
        blueprint.Unarchive(_clock.UtcNow);
        blueprint.Deactivate(_clock.UtcNow);

        blueprint.Archive(_clock.UtcNow);
        blueprint.PreviousStatus.Should().Be(AttributeBlueprintStatus.Inactive);
        blueprint.Unarchive(_clock.UtcNow);

        blueprint.Status.Should().Be(AttributeBlueprintStatus.Inactive);
        blueprint.PreviousStatus.Should().BeNull();
    }

    [Fact]
    public void Delete_Clears_Saved_Archive_Status()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        blueprint.Archive(_clock.UtcNow);

        blueprint.Delete(_clock.UtcNow);

        blueprint.Status.Should().Be(AttributeBlueprintStatus.Inactive);
        blueprint.PreviousStatus.Should().BeNull();
        blueprint.DeletedAt.Should().Be(_clock.UtcNow);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    public async Task Archive_And_Unarchive_Reject_Invalid_Id_Before_Loading(bool archive, long id)
    {
        var service = CreateService();

        var result = archive
            ? await service.ArchiveAsync(id, TestContext.Current.CancellationToken)
            : await service.UnarchiveAsync(id, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("VALIDATION_FAILED");
        _repository.ReceivedCalls().Should().BeEmpty();
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Archive_And_Unarchive_Reject_Missing_Or_Deleted_Blueprint_Without_Saving(bool archive, bool deleted)
    {
        if (deleted)
        {
            var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
            blueprint.Delete(_clock.UtcNow);
            _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        }
        var service = CreateService();

        var result = archive
            ? await service.ArchiveAsync(BlueprintId, TestContext.Current.CancellationToken)
            : await service.UnarchiveAsync(BlueprintId, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(deleted ? "ATTRIBUTE_BLUEPRINT_DELETED" : "ATTRIBUTE_BLUEPRINT_NOT_FOUND");
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Status_Toggle_Cannot_Bypass_Unarchive(bool activate)
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Color", AttributeBlueprintDataType.Text, _clock.UtcNow);
        blueprint.Deactivate(_clock.UtcNow);
        blueprint.Archive(_clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        var clock = Substitute.For<IClock>();
        var service = CreateService(clock);

        var result = activate
            ? await service.ActivateAsync(BlueprintId, TestContext.Current.CancellationToken)
            : await service.DeactivateAsync(BlueprintId, TestContext.Current.CancellationToken);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_ARCHIVED");
        blueprint.Status.Should().Be(AttributeBlueprintStatus.Archived);
        _unitOfWork.ReceivedCalls().Should().BeEmpty();
        clock.ReceivedCalls().Should().BeEmpty("the service must reject the request before invoking the domain status change");
    }

    [Fact]
    public async Task Create_Refuses_A_Name_That_Canonicalises_Onto_An_Existing_One()
    {
        _idGenerator.Generate().Returns(BlueprintId);
        _repository.NameExistsAsync("screen size", null, Arg.Any<CancellationToken>()).Returns(false);
        _repository.CanonicalKeyExistsAsync("screen-size", null, null, Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService();

        var result = await service.CreateAsync("screen size", AttributeBlueprintDataType.Text, [],
            TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_KEY_TAKEN",
            "'screen size' and 'Screen Size' are different names but one key, so the name check "
            + "above it does not cover what the uniqueness index enforces");
        await _repository.DidNotReceive().AddAsync(Arg.Any<AttributeBlueprint>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetScope_Narrows_The_Attribute_To_A_Category_And_Saves()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Screen Size", AttributeBlueprintDataType.Number, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _categoryRepository.GetByIdAsync(7, Arg.Any<CancellationToken>())
            .Returns(Category.Create(7, "Monitors", "monitors", null, null, 0, _clock.UtcNow));
        var service = CreateService();

        var result = await service.SetScopeAsync(BlueprintId, AttributeScope.Category, 7,
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.Scope.Should().Be(AttributeScope.Category);
        blueprint.ScopeCategoryId.Should().Be(7);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetScope_Refuses_A_Category_That_Does_Not_Exist()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Screen Size", AttributeBlueprintDataType.Number, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _categoryRepository.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns((Category?)null);
        var service = CreateService();

        var result = await service.SetScopeAsync(BlueprintId, AttributeScope.Category, 7,
            TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
        blueprint.Scope.Should().Be(AttributeScope.Global);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetScope_Refuses_A_Move_Into_A_Scope_That_Already_Holds_The_Key()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Screen Size", AttributeBlueprintDataType.Number, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _categoryRepository.GetByIdAsync(7, Arg.Any<CancellationToken>())
            .Returns(Category.Create(7, "Monitors", "monitors", null, null, 0, _clock.UtcNow));
        _repository.CanonicalKeyExistsAsync("screen-size", 7, BlueprintId, Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService();

        var result = await service.SetScopeAsync(BlueprintId, AttributeScope.Category, 7,
            TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_KEY_TAKEN",
            "the admin must read a conflict rather than a raw unique-violation from the database");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetUnit_Refuses_A_Unit_That_Does_Not_Exist()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Screen Size", AttributeBlueprintDataType.Number, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _measurementUnitRepository.ExistsAsync(9, Arg.Any<CancellationToken>()).Returns(false);
        var service = CreateService();

        var result = await service.SetUnitAsync(BlueprintId, 9, TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("MEASUREMENT_UNIT_NOT_FOUND");
        blueprint.UnitId.Should().BeNull();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetUnit_Records_An_Existing_Unit()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Screen Size", AttributeBlueprintDataType.Number, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        _measurementUnitRepository.ExistsAsync(9, Arg.Any<CancellationToken>()).Returns(true);
        var service = CreateService();

        var result = await service.SetUnitAsync(BlueprintId, 9, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.UnitId.Should().Be(9);
    }

    [Fact]
    public async Task SetUnit_Clearing_Does_Not_Consult_The_Unit_Table()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Screen Size", AttributeBlueprintDataType.Number, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        var service = CreateService();

        var result = await service.SetUnitAsync(BlueprintId, null, TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        await _measurementUnitRepository.DidNotReceive().ExistsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAliases_Replaces_The_List_And_Saves()
    {
        var blueprint = AttributeBlueprint.Create(BlueprintId, "Screen Size", AttributeBlueprintDataType.Number, _clock.UtcNow);
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns(blueprint);
        var service = CreateService();

        var result = await service.SetAliasesAsync(BlueprintId, ["Display Size"], TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeTrue();
        blueprint.Aliases.Should().BeEquivalentTo(["display-size"]);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetAliases_Reports_A_Missing_Attribute_Rather_Than_Saving()
    {
        _repository.GetByIdAsync(BlueprintId, Arg.Any<CancellationToken>()).Returns((AttributeBlueprint?)null);
        var service = CreateService();

        var result = await service.SetAliasesAsync(BlueprintId, ["Display Size"], TestContext.Current.CancellationToken);

        result.Error!.Code.Should().Be("ATTRIBUTE_BLUEPRINT_NOT_FOUND");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private AttributeBlueprintService CreateService(IClock? clock = null)
        => new(
            _repository,
            _measurementUnitRepository,
            _categoryRepository,
            _unitOfWork,
            _dependencyGateway,
            clock ?? _clock,
            _idGenerator,
            new CreateAttributeBlueprintValidator(),
            new RenameAttributeBlueprintValidator(),
            new ChangeAttributeBlueprintDataTypeValidator(),
            new AttributeBlueprintIdValidator(),
            new AttributeBlueprintOptionInputValidator(),
            new AttributeBlueprintOptionIdsValidator(),
            new UpdateAttributeBlueprintValidator());
}
