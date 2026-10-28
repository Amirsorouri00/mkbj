using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Services;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Validation;
using EwaysVNext.Persistence;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

public class CategoryAttributeExclusionServiceTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();
    private static long NextId() => IdGenerator.Generate();

    private readonly ICategoryAttributeExclusionRepository _exclusions = Substitute.For<ICategoryAttributeExclusionRepository>();
    private readonly ICategoryAttributeAssignmentRepository _assignments = Substitute.For<ICategoryAttributeAssignmentRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedClock _clock = new();

    private CategoryAttributeExclusionService CreateSut()
        => new(_exclusions, _assignments, _categories, _unitOfWork, _clock, IdGenerator, new CategoryAttributeExclusionValidator());

    private void SetAncestry(long categoryId, long parentId, long grandparentId)
    {
        _categories.GetHierarchyAsync(categoryId, null, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CategoryHierarchyNode(categoryId, parentId),
            new CategoryHierarchyNode(parentId, grandparentId),
            new CategoryHierarchyNode(grandparentId, null),
        });
    }

    [Fact]
    public async Task ExcludeAsync_Fails_Validation_When_CategoryId_Is_Not_Positive()
    {
        var sut = CreateSut();

        var result = await sut.ExcludeAsync(0, NextId());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("VALIDATION");
        await _assignments.DidNotReceive().GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExcludeAsync_Fails_When_Source_Assignment_Is_Missing_Or_Removed()
    {
        var sut = CreateSut();
        _assignments.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns((CategoryAttributeAssignment?)null);

        var result = await sut.ExcludeAsync(NextId(), NextId());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_ASSIGNMENT_NOT_FOUND");
    }

    [Fact]
    public async Task ExcludeAsync_Fails_When_Source_Assignment_Belongs_To_This_Category_Itself()
    {
        var categoryId = NextId();
        var ownAssignment = CategoryAttributeAssignment.Create(NextId(), categoryId, NextId(), TestTime.Now);
        _assignments.GetByIdAsync(ownAssignment.Id, Arg.Any<CancellationToken>()).Returns(ownAssignment);
        var sut = CreateSut();

        var result = await sut.ExcludeAsync(categoryId, ownAssignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_INHERITED");
    }

    [Fact]
    public async Task ExcludeAsync_Fails_When_Source_Category_Is_Not_A_Real_Ancestor()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var unrelatedCategoryId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);

        var unrelatedAssignment = CategoryAttributeAssignment.Create(NextId(), unrelatedCategoryId, NextId(), TestTime.Now);
        _assignments.GetByIdAsync(unrelatedAssignment.Id, Arg.Any<CancellationToken>()).Returns(unrelatedAssignment);
        var sut = CreateSut();

        var result = await sut.ExcludeAsync(categoryId, unrelatedAssignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_AN_ANCESTOR");
    }

    [Fact]
    public async Task ExcludeAsync_Fails_When_A_Nearer_Ancestor_Assignment_Shadows_The_Requested_One()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);

        var grandparentAssignment = CategoryAttributeAssignment.Create(NextId(), grandparentId, attributeBlueprintId, TestTime.Now);
        var parentAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, attributeBlueprintId, TestTime.Now);
        _assignments.GetByIdAsync(grandparentAssignment.Id, Arg.Any<CancellationToken>()).Returns(grandparentAssignment);
        _assignments.ListByCategoryAsync(parentId, Arg.Any<CancellationToken>()).Returns(new[] { parentAssignment });
        _assignments.ListByCategoryAsync(grandparentId, Arg.Any<CancellationToken>()).Returns(new[] { grandparentAssignment });
        var sut = CreateSut();

        var result = await sut.ExcludeAsync(categoryId, grandparentAssignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_SHADOWED");
    }

    [Fact]
    public async Task ExcludeAsync_Succeeds_For_The_Nearest_Ancestor_Assignment_And_Saves()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);

        var grandparentAssignment = CategoryAttributeAssignment.Create(NextId(), grandparentId, attributeBlueprintId, TestTime.Now);
        var parentAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, attributeBlueprintId, TestTime.Now);
        _assignments.GetByIdAsync(parentAssignment.Id, Arg.Any<CancellationToken>()).Returns(parentAssignment);
        _assignments.ListByCategoryAsync(parentId, Arg.Any<CancellationToken>()).Returns(new[] { parentAssignment });
        _assignments.ListByCategoryAsync(grandparentId, Arg.Any<CancellationToken>()).Returns(new[] { grandparentAssignment });
        _exclusions.GetByCategoryAndSourceAsync(categoryId, parentAssignment.Id, Arg.Any<CancellationToken>())
            .Returns((CategoryAttributeExclusion?)null);
        var sut = CreateSut();

        var result = await sut.ExcludeAsync(categoryId, parentAssignment.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsExcluded.Should().BeTrue();
        await _exclusions.Received(1).AddAsync(
            Arg.Is<CategoryAttributeExclusion>(e => e.CategoryId == categoryId && e.SourceAssignmentId == parentAssignment.Id),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExcludeAsync_Fails_When_Already_Excluded()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);

        var parentAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, attributeBlueprintId, TestTime.Now);
        _assignments.GetByIdAsync(parentAssignment.Id, Arg.Any<CancellationToken>()).Returns(parentAssignment);
        _assignments.ListByCategoryAsync(parentId, Arg.Any<CancellationToken>()).Returns(new[] { parentAssignment });
        _assignments.ListByCategoryAsync(grandparentId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeAssignment>());
        _exclusions.GetByCategoryAndSourceAsync(categoryId, parentAssignment.Id, Arg.Any<CancellationToken>())
            .Returns(CategoryAttributeExclusion.Create(NextId(), categoryId, parentAssignment.Id, TestTime.Now));
        var sut = CreateSut();

        var result = await sut.ExcludeAsync(categoryId, parentAssignment.Id);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_ALREADY_EXCLUDED");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncludeAsync_Fails_When_Not_Currently_Excluded()
    {
        var sut = CreateSut();
        _exclusions.GetByCategoryAndSourceAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((CategoryAttributeExclusion?)null);

        var result = await sut.IncludeAsync(NextId(), NextId());

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_NOT_FOUND");
    }

    [Fact]
    public async Task IncludeAsync_Removes_The_Existing_Exclusion_And_Saves()
    {
        var categoryId = NextId();
        var sourceAssignmentId = NextId();
        var existing = CategoryAttributeExclusion.Create(NextId(), categoryId, sourceAssignmentId, TestTime.Now);
        _exclusions.GetByCategoryAndSourceAsync(categoryId, sourceAssignmentId, Arg.Any<CancellationToken>()).Returns(existing);
        var sut = CreateSut();

        var result = await sut.IncludeAsync(categoryId, sourceAssignmentId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsExcluded.Should().BeFalse();
        await _exclusions.Received(1).RemoveAsync(existing, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExclusionsAsync_Fails_Validation_When_CategoryId_Is_Not_Positive()
    {
        var sut = CreateSut();

        var result = await sut.SetExclusionsAsync(0, new[] { NextId() });

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_CATEGORY_INVALID");
        await _exclusions.DidNotReceive().ListByCategoryAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExclusionsAsync_Adds_New_Exclusions_For_Valid_Nearest_Ancestor_Assignments()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);

        var parentAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, attributeBlueprintId, TestTime.Now);
        _assignments.GetByIdAsync(parentAssignment.Id, Arg.Any<CancellationToken>()).Returns(parentAssignment);
        _assignments.ListByCategoryAsync(parentId, Arg.Any<CancellationToken>()).Returns(new[] { parentAssignment });
        _assignments.ListByCategoryAsync(grandparentId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeAssignment>());
        _exclusions.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeExclusion>());
        var sut = CreateSut();

        var result = await sut.SetExclusionsAsync(categoryId, new[] { parentAssignment.Id });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(s => s.SourceAssignmentId == parentAssignment.Id && s.IsExcluded);
        await _exclusions.Received(1).AddAsync(
            Arg.Is<CategoryAttributeExclusion>(e => e.CategoryId == categoryId && e.SourceAssignmentId == parentAssignment.Id),
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExclusionsAsync_Removes_Exclusions_No_Longer_In_The_Desired_Set()
    {
        var categoryId = NextId();
        var existing = CategoryAttributeExclusion.Create(NextId(), categoryId, NextId(), TestTime.Now);
        _exclusions.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(new[] { existing });
        var sut = CreateSut();

        var result = await sut.SetExclusionsAsync(categoryId, Array.Empty<long>());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().BeEmpty();
        await _exclusions.Received(1).RemoveAsync(existing, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExclusionsAsync_Is_AllOrNothing_When_One_New_Exclusion_Is_Invalid()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);

        var validAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, NextId(), TestTime.Now);
        var grandparentAssignment = CategoryAttributeAssignment.Create(NextId(), grandparentId, attributeBlueprintId, TestTime.Now);
        var shadowingParentAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, attributeBlueprintId, TestTime.Now);
        _assignments.GetByIdAsync(validAssignment.Id, Arg.Any<CancellationToken>()).Returns(validAssignment);
        _assignments.GetByIdAsync(grandparentAssignment.Id, Arg.Any<CancellationToken>()).Returns(grandparentAssignment);
        _assignments.ListByCategoryAsync(parentId, Arg.Any<CancellationToken>())
            .Returns(new[] { validAssignment, shadowingParentAssignment });
        _assignments.ListByCategoryAsync(grandparentId, Arg.Any<CancellationToken>()).Returns(new[] { grandparentAssignment });

        var existing = CategoryAttributeExclusion.Create(NextId(), categoryId, NextId(), TestTime.Now);
        _exclusions.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(new[] { existing });
        var sut = CreateSut();

        var result = await sut.SetExclusionsAsync(categoryId, new[] { validAssignment.Id, grandparentAssignment.Id });

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_ATTRIBUTE_EXCLUSION_SHADOWED");
        await _exclusions.DidNotReceive().AddAsync(Arg.Any<CategoryAttributeExclusion>(), Arg.Any<CancellationToken>());
        await _exclusions.DidNotReceive().RemoveAsync(Arg.Any<CategoryAttributeExclusion>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExclusionsAsync_Is_A_NoOp_When_The_Desired_Set_Already_Matches_Current()
    {
        var categoryId = NextId();
        var sourceAssignmentId = NextId();
        var existing = CategoryAttributeExclusion.Create(NextId(), categoryId, sourceAssignmentId, TestTime.Now);
        _exclusions.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(new[] { existing });
        var sut = CreateSut();

        var result = await sut.SetExclusionsAsync(categoryId, new[] { sourceAssignmentId });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(s => s.SourceAssignmentId == sourceAssignmentId && s.IsExcluded);
        await _exclusions.DidNotReceive().AddAsync(Arg.Any<CategoryAttributeExclusion>(), Arg.Any<CancellationToken>());
        await _exclusions.DidNotReceive().RemoveAsync(Arg.Any<CategoryAttributeExclusion>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
