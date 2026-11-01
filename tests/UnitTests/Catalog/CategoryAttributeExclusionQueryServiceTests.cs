using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Domain;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeAssignments.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Domain;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.CategoryAttributeExclusions.Services;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using NSubstitute;
using Xunit;

namespace EwaysVNext.UnitTests.Catalog;

public class CategoryAttributeExclusionQueryServiceTests
{
    private static readonly SnowflakeIdGenerator IdGenerator = new();
    private static long NextId() => IdGenerator.Generate();

    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ICategoryAttributeAssignmentRepository _assignments = Substitute.For<ICategoryAttributeAssignmentRepository>();
    private readonly ICategoryAttributeExclusionRepository _exclusions = Substitute.For<ICategoryAttributeExclusionRepository>();
    private readonly IAttributeBlueprintRepository _blueprints = Substitute.For<IAttributeBlueprintRepository>();

    private CategoryAttributeExclusionQueryService CreateSut()
        => new(_categories, _assignments, _exclusions, _blueprints);

    private void SetAncestry(long categoryId, long parentId, long grandparentId)
    {
        _categories.GetHierarchyAsync(categoryId, null, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CategoryHierarchyNode(categoryId, parentId),
            new CategoryHierarchyNode(parentId, grandparentId),
            new CategoryHierarchyNode(grandparentId, null),
        });
    }

    private static AttributeBlueprintSnapshot BlueprintSnapshot(long id, string name)
        => new(id, name, name.ToLowerInvariant(), "Text", "Active", "Global", null, [], null, null, TestTime.Now, null,
            Array.Empty<AttributeBlueprintOptionSnapshot>());

    [Fact]
    public async Task GetEffectiveAttributesAsync_Returns_Direct_Assignment_As_Not_Inherited_And_Not_Excluded()
    {
        var categoryId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, NextId(), NextId());
        var direct = CategoryAttributeAssignment.Create(NextId(), categoryId, attributeBlueprintId, TestTime.Now);
        _assignments.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(new[] { direct });
        _exclusions.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeExclusion>());
        _blueprints.GetSnapshotsByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(new[] { BlueprintSnapshot(attributeBlueprintId, "Color") });
        var sut = CreateSut();

        var result = await sut.GetEffectiveAttributesAsync(categoryId);

        result.Should().ContainSingle(a =>
            a.AssignmentId == direct.Id &&
            a.SourceCategoryId == categoryId &&
            a.AttributeBlueprintName == "Color" &&
            !a.IsInherited &&
            !a.IsExcluded);
    }

    [Fact]
    public async Task GetEffectiveAttributesAsync_Returns_Inherited_Assignment_As_Not_Excluded_By_Default()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);
        var parentAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, attributeBlueprintId, TestTime.Now);
        _assignments.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeAssignment>());
        _assignments.ListByCategoryAsync(parentId, Arg.Any<CancellationToken>()).Returns(new[] { parentAssignment });
        _assignments.ListByCategoryAsync(grandparentId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeAssignment>());
        _exclusions.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeExclusion>());
        _blueprints.GetSnapshotsByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AttributeBlueprintSnapshot>());
        var sut = CreateSut();

        var result = await sut.GetEffectiveAttributesAsync(categoryId);

        result.Should().ContainSingle(a =>
            a.AssignmentId == parentAssignment.Id &&
            a.SourceCategoryId == parentId &&
            a.IsInherited &&
            !a.IsExcluded);
    }

    [Fact]
    public async Task GetEffectiveAttributesAsync_Marks_Inherited_Assignment_As_Excluded_When_An_Exclusion_Record_Exists()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);
        var parentAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, attributeBlueprintId, TestTime.Now);
        _assignments.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeAssignment>());
        _assignments.ListByCategoryAsync(parentId, Arg.Any<CancellationToken>()).Returns(new[] { parentAssignment });
        _assignments.ListByCategoryAsync(grandparentId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeAssignment>());
        var existingExclusion = CategoryAttributeExclusion.Create(NextId(), categoryId, parentAssignment.Id, TestTime.Now);
        _exclusions.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(new[] { existingExclusion });
        _blueprints.GetSnapshotsByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AttributeBlueprintSnapshot>());
        var sut = CreateSut();

        var result = await sut.GetEffectiveAttributesAsync(categoryId);

        result.Should().ContainSingle(a => a.AssignmentId == parentAssignment.Id && a.IsInherited && a.IsExcluded);
    }

    [Fact]
    public async Task GetEffectiveAttributesAsync_Prefers_The_Nearest_Ancestor_Assignment_When_Both_Assign_The_Same_Attribute()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);
        var parentAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, attributeBlueprintId, TestTime.Now);
        var grandparentAssignment = CategoryAttributeAssignment.Create(NextId(), grandparentId, attributeBlueprintId, TestTime.Now);
        _assignments.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeAssignment>());
        _assignments.ListByCategoryAsync(parentId, Arg.Any<CancellationToken>()).Returns(new[] { parentAssignment });
        _assignments.ListByCategoryAsync(grandparentId, Arg.Any<CancellationToken>()).Returns(new[] { grandparentAssignment });
        _exclusions.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeExclusion>());
        _blueprints.GetSnapshotsByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AttributeBlueprintSnapshot>());
        var sut = CreateSut();

        var result = await sut.GetEffectiveAttributesAsync(categoryId);

        result.Should().ContainSingle(a => a.AttributeBlueprintId == attributeBlueprintId);
        result.Single(a => a.AttributeBlueprintId == attributeBlueprintId).AssignmentId.Should().Be(parentAssignment.Id);
    }

    [Fact]
    public async Task GetEffectiveAttributesAsync_Excludes_Removed_Ancestor_Assignments()
    {
        var categoryId = NextId();
        var parentId = NextId();
        var grandparentId = NextId();
        var attributeBlueprintId = NextId();
        SetAncestry(categoryId, parentId, grandparentId);
        var removedAssignment = CategoryAttributeAssignment.Create(NextId(), parentId, attributeBlueprintId, TestTime.Now);
        removedAssignment.Remove(TestTime.Now);
        _assignments.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeAssignment>());
        _assignments.ListByCategoryAsync(parentId, Arg.Any<CancellationToken>()).Returns(new[] { removedAssignment });
        _assignments.ListByCategoryAsync(grandparentId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeAssignment>());
        _exclusions.ListByCategoryAsync(categoryId, Arg.Any<CancellationToken>()).Returns(Array.Empty<CategoryAttributeExclusion>());
        _blueprints.GetSnapshotsByIdsAsync(Arg.Any<IReadOnlyCollection<long>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<AttributeBlueprintSnapshot>());
        var sut = CreateSut();

        var result = await sut.GetEffectiveAttributesAsync(categoryId);

        result.Should().BeEmpty();
    }
}
