using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Services;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Validation;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

public class CategoryAttributeAssignmentServiceTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();
    private static long NextId() => IdGenerator.Generate();

    private static CategoryAttributeAssignmentService CreateSut(
        ICategoryAttributeAssignmentRepository repo, IUnitOfWork? uow = null, FixedClock? clock = null)
        => new(repo, uow ?? Substitute.For<IUnitOfWork>(), clock ?? new FixedClock(), IdGenerator,
            new CategoryAttributeAssignmentValidator(), new ReorderCategoryAttributeValidator());

    private static ICategoryAttributeAssignmentRepository CreateRepoAllowingAssign()
    {
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetActiveByCategoryAndAttributeAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((CategoryAttributeAssignment?)null);
        repo.CountActivePrimaryInCategoriesAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(0);
        return repo;
    }

    [Fact]
    public async Task AssignAsync_Fails_Validation_When_CategoryId_Is_Not_Positive()
    {
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        var sut = CreateSut(repo);

        var result = await sut.AssignAsync(0, NextId(), false, false, false, false, null, false, false);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("VALIDATION");
        await repo.DidNotReceive().GetActiveByCategoryAndAttributeAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignAsync_Fails_Validation_When_TitleOrder_Is_Negative()
    {
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        var sut = CreateSut(repo);

        var result = await sut.AssignAsync(NextId(), NextId(), false, false, false, true, -1, false, false);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("VALIDATION");
    }

    [Fact]
    public async Task AssignAsync_Fails_When_Already_Actively_Assigned()
    {
        var categoryId = NextId();
        var attributeId = NextId();
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetActiveByCategoryAndAttributeAsync(categoryId, attributeId, Arg.Any<CancellationToken>())
            .Returns(CategoryAttributeAssignment.Create(NextId(), categoryId, attributeId, TestTime.Now));

        var sut = CreateSut(repo);
        var result = await sut.AssignAsync(categoryId, attributeId, false, false, false, false, null, false, false);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_ALREADY_ASSIGNED");
    }

    [Fact]
    public async Task AssignAsync_Allows_All_Flags_True_Simultaneously()
    {
        var repo = CreateRepoAllowingAssign();
        var uow = Substitute.For<IUnitOfWork>();

        var sut = CreateSut(repo, uow);
        var result = await sut.AssignAsync(NextId(), NextId(), true, true, true, true, 1, false, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsRequired.Should().BeTrue();
        result.Value.IsFilterable.Should().BeTrue();
        result.Value.IsSearchable.Should().BeTrue();
        result.Value.IsInTitle.Should().BeTrue();
        result.Value.TitleOrder.Should().Be(1);
        await repo.Received(1).AddAsync(Arg.Any<CategoryAttributeAssignment>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignAsync_With_Primary_Forces_Required_On_Snapshot()
    {
        var repo = CreateRepoAllowingAssign();
        var sut = CreateSut(repo);

        var result = await sut.AssignAsync(NextId(), NextId(), false, false, false, false, null, true, false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsPrimary.Should().BeTrue();
        result.Value.IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task AssignAsync_Fails_When_Primary_Limit_Already_Reached()
    {
        var categoryId = NextId();
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetActiveByCategoryAndAttributeAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((CategoryAttributeAssignment?)null);
        repo.CountActivePrimaryInCategoriesAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(CategoryAttributeAssignment.MaxPrimaryAttributesPerCategory);

        var sut = CreateSut(repo);
        var result = await sut.AssignAsync(categoryId, NextId(), false, false, false, false, null, true, false);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_PRIMARY_LIMIT_REACHED");
        await repo.DidNotReceive().AddAsync(Arg.Any<CategoryAttributeAssignment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetRequiredAsync_Returns_NotFound_When_Missing()
    {
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns((CategoryAttributeAssignment?)null);

        var sut = CreateSut(repo);
        var result = await sut.SetRequiredAsync(NextId(), true);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_ASSIGNMENT_NOT_FOUND");
    }

    [Fact]
    public async Task SetRequiredAsync_Mutates_And_Saves()
    {
        var assignment = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isRequired: false);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);
        var uow = Substitute.For<IUnitOfWork>();

        var sut = CreateSut(repo, uow);
        var result = await sut.SetRequiredAsync(assignment.Id, true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsRequired.Should().BeTrue();
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetRequiredAsync_Fails_When_Locked_By_Primary()
    {
        var assignment = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isPrimary: true);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        var sut = CreateSut(repo);
        var result = await sut.SetRequiredAsync(assignment.Id, false);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_REQUIRED_LOCKED_BY_PRIMARY");
    }

    [Fact]
    public async Task SetFilterableAsync_And_SetSearchableAsync_Mutate_Independently()
    {
        var assignment = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isRequired: true);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        var sut = CreateSut(repo);
        await sut.SetFilterableAsync(assignment.Id, true);
        var result = await sut.SetSearchableAsync(assignment.Id, true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsFilterable.Should().BeTrue();
        result.Value.IsSearchable.Should().BeTrue();
        result.Value.IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task UnassignAsync_Sets_IsRemoved_On_Snapshot()
    {
        var assignment = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        var sut = CreateSut(repo);
        var result = await sut.UnassignAsync(assignment.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsRemoved.Should().BeTrue();
    }

    [Fact]
    public async Task ReorderAsync_Fails_Validation_When_TitleOrder_Is_Negative()
    {
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        var sut = CreateSut(repo);

        var result = await sut.ReorderAsync(NextId(), -1);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("VALIDATION");
        await repo.DidNotReceive().GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReorderAsync_Fails_When_Not_InTitle()
    {
        var assignment = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isInTitle: false);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        var sut = CreateSut(repo);
        var result = await sut.ReorderAsync(assignment.Id, 2);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_NOT_IN_TITLE");
    }

    [Fact]
    public async Task SetPrimaryAsync_Enabling_Succeeds_And_Forces_Required()
    {
        var assignment = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);
        repo.CountActivePrimaryInCategoriesAsync(Arg.Any<IReadOnlyList<long>>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(0);

        var sut = CreateSut(repo);
        var result = await sut.SetPrimaryAsync(assignment.Id, true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsPrimary.Should().BeTrue();
        result.Value.IsRequired.Should().BeTrue();
    }

    [Fact]
    public async Task SetPrimaryAsync_Fails_When_Limit_Reached_Excluding_Itself()
    {
        var assignment = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);
        repo.CountActivePrimaryInCategoriesAsync(Arg.Any<IReadOnlyList<long>>(), assignment.Id, Arg.Any<CancellationToken>())
            .Returns(CategoryAttributeAssignment.MaxPrimaryAttributesPerCategory);

        var sut = CreateSut(repo);
        var result = await sut.SetPrimaryAsync(assignment.Id, true);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_PRIMARY_LIMIT_REACHED");
        await repo.Received(1).CountActivePrimaryInCategoriesAsync(
            Arg.Is<IReadOnlyList<long>>(ids => ids.Count == 1 && ids[0] == assignment.CategoryId),
            assignment.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetPrimaryAsync_Fails_When_VariantGenerating_Is_Enabled()
    {
        var assignment = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isVariantGenerating: true);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        var sut = CreateSut(repo);
        var result = await sut.SetPrimaryAsync(assignment.Id, true);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_PRIMARY_BLOCKED_BY_VARIANT_GENERATING");
    }

    [Fact]
    public async Task SetVariantGeneratingAsync_Enabling_Disables_Primary_On_Snapshot()
    {
        var assignment = CategoryAttributeAssignment.Create(NextId(), NextId(), NextId(), TestTime.Now, isPrimary: true);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.GetByIdAsync(assignment.Id, Arg.Any<CancellationToken>()).Returns(assignment);

        var sut = CreateSut(repo);
        var result = await sut.SetVariantGeneratingAsync(assignment.Id, true);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsVariantGenerating.Should().BeTrue();
        result.Value.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public async Task ListByCategoryAsync_Maps_Aggregates_To_Snapshots()
    {
        var categoryId = NextId();
        var assignment = CategoryAttributeAssignment.Create(NextId(), categoryId, NextId(), TestTime.Now, isFilterable: true);
        var repo = Substitute.For<ICategoryAttributeAssignmentRepository>();
        repo.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>())
            .Returns(new[] { assignment });

        var sut = CreateSut(repo);
        var result = await sut.ListByCategoryAsync(categoryId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(s => s.IsFilterable && s.CategoryId == categoryId);
    }
}
