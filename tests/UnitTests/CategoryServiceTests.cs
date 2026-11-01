using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Domain.Events;
using EwaysVNext.Outcomes;
using EwaysVNext.Modules.Catalog.Application.Categories.Commands;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.Categories.Services;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using Microsoft.Extensions.DependencyInjection;
using EwaysVNext.Modules.Catalog.Application.Categories;
using Xunit;
using EwaysVNext.Persistence;

namespace EwaysVNext.UnitTests;

public sealed class CategoryServiceTests
{
    private const long GeneratedCategoryId = 912345678901L;
    //private static readonly Guid ActiveProductId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private const long ActiveProductId = 1;
    //private static readonly Guid MissingProductId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private const long MissingProductId = 2;

    [Fact]
    public async Task Create_Must_Return_The_Generated_Id_After_Persistence()
    {
        var log = new List<string>();
        var categories = new InMemoryCategoryRepository(log);
        var assignments = new InMemoryCategoryProductRepository(log);
        var unitOfWork = new InMemoryCategoryUnitOfWork(log);
        var catalog = new StubCatalogProductLookup(ActiveProductId, log);
        var idGenerator = new FixedIdGenerator(GeneratedCategoryId);
        var constructor = typeof(CategoryService).GetConstructors().Single();
        var arguments = constructor.GetParameters().Select(ResolveArgument).ToArray();
        var service = (ICategoryService)constructor.Invoke(arguments);

        var result = await service.CreateAsync(
            new CreateCategoryCommand("Category", "category", null, null, 0));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(GeneratedCategoryId);
        categories.All.Should().ContainSingle(category => category.Id == GeneratedCategoryId);
        log.Should().ContainInOrder("save");

        object ResolveArgument(System.Reflection.ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(ICategoryRepository)) return categories;
            if (parameter.ParameterType == typeof(ICategoryProductRepository)) return assignments;
            if (parameter.ParameterType == typeof(IUnitOfWork)) return unitOfWork;
            if (parameter.ParameterType == typeof(ICatalogProductLookup)) return catalog;
            if (parameter.ParameterType == typeof(IClock)) return new FixedClock();
            if (parameter.ParameterType == typeof(IIdGenerator)) return idGenerator;
            throw new InvalidOperationException($"Unexpected dependency {parameter.ParameterType}.");
        }
    }

    [Fact]
    public async Task Create_Must_Reject_A_Missing_Parent()
    {
        var fixture = new Fixture();
        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Child", "child", null, 999, 0));

        result.Error!.Code.Should().Be("CATEGORY_PARENT_INVALID");
        fixture.Categories.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_Must_Allow_An_Inactive_Parent()
    {
        var fixture = new Fixture();
        var parent = fixture.AddCategory("Parent", "parent");
        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Child", "child", null, parent.Id, 0));

        result.IsSuccess.Should().BeTrue();
        fixture.Categories.All.Should().HaveCount(2);
        fixture.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_Must_Reject_Level_Six()
    {
        var fixture = new Fixture();
        var levelOne = fixture.AddCategory("Level 1", "level-1");
        var levelTwo = fixture.AddCategory("Level 2", "level-2", levelOne.Id);
        var levelThree = fixture.AddCategory("Level 3", "level-3", levelTwo.Id);
        var levelFour = fixture.AddCategory("Level 4", "level-4", levelThree.Id);
        var levelFive = fixture.AddCategory("Level 5", "level-5", levelFour.Id);

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Level 6", "level-6", null, levelFive.Id, 0));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_DEPTH_EXCEEDED");
        result.Error.Message.Should().Be("A category tree cannot be deeper than five levels.");
        fixture.Categories.All.Should().HaveCount(5);
    }

    [Fact]
    public async Task Create_Must_Allow_Level_Five_When_Root_Is_Level_One()
    {
        var fixture = new Fixture();
        var levelOne = fixture.AddCategory("Level 1", "level-1");
        var levelTwo = fixture.AddCategory("Level 2", "level-2", levelOne.Id);
        var levelThree = fixture.AddCategory("Level 3", "level-3", levelTwo.Id);
        var levelFour = fixture.AddCategory("Level 4", "level-4", levelThree.Id);

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Level 5", "level-5", null, levelFour.Id, 0));

        result.IsSuccess.Should().BeTrue();
        fixture.Categories.All.Should().ContainSingle(category => category.Id == result.Value);
        fixture.Categories.HierarchyLoadCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_Must_Terminate_And_Reject_A_Stored_Ancestor_Cycle()
    {
        var fixture = new Fixture();
        var first = fixture.AddCategory("First", "first");
        var second = fixture.AddCategory("Second", "second", first.Id);
        typeof(Category).GetProperty(nameof(Category.ParentId))!
            .SetValue(first, second.Id);

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Child", "child", null, second.Id, 0));

        result.Error!.Code.Should().Be("CATEGORY_CYCLE");
        fixture.Categories.HierarchyLoadCount.Should().Be(1);
    }

    [Fact]
    public async Task Deleted_Intermediate_Nodes_Must_Still_Count_Toward_Depth()
    {
        var fixture = new Fixture();
        var levelOne = fixture.AddCategory("Level 1", "level-1");
        var levelTwo = fixture.AddCategory("Level 2", "level-2", levelOne.Id);
        levelTwo.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, TestTime.Now.AddHours(-1));
        var levelThree = fixture.AddCategory("Level 3", "level-3", levelTwo.Id);
        var levelFour = fixture.AddCategory("Level 4", "level-4", levelThree.Id);
        var levelFive = fixture.AddCategory("Level 5", "level-5", levelFour.Id);

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Level 6", "level-6", null, levelFive.Id, 0));

        result.Error!.Code.Should().Be("CATEGORY_DEPTH_EXCEEDED");
        fixture.Categories.HierarchyLoadCount.Should().Be(1);
    }

    [Fact]
    public async Task Deleted_Parent_Must_Be_Invalid_For_Create_And_Move()
    {
        var fixture = new Fixture();
        var parent = fixture.AddCategory("Parent", "parent");
        parent.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, TestTime.Now.AddHours(-1));
        var category = fixture.AddCategory("Category", "category");
        var create = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Child", "child", null, parent.Id, 0));
        var move = await fixture.Sut.MoveAsync(new MoveCategoryCommand(category.Id, parent.Id, 0));

        create.Error!.Code.Should().Be("CATEGORY_PARENT_INVALID");
        move.Error!.Code.Should().Be("CATEGORY_PARENT_INVALID");
    }

    [Fact]
    public async Task Activation_Must_Require_An_Active_Parent()
    {
        var fixture = new Fixture();
        var parent = fixture.AddCategory("Parent", "parent");
        var child = fixture.AddCategory("Child", "child", parent.Id);
        var result = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(child.Id, CategoryStatus.Active));

        result.Error!.Code.Should().Be("CATEGORY_PARENT_INACTIVE");
        child.Status.Should().Be(CategoryStatus.Inactive);
    }

    [Fact]
    public async Task SetStatus_Active_Must_Require_An_Active_Parent()
    {
        var fixture = new Fixture();
        var parent = fixture.AddCategory("Parent", "parent");
        var child = fixture.AddCategory("Child", "child", parent.Id);

        var result = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(child.Id, CategoryStatus.Active));

        result.Error!.Code.Should().Be("CATEGORY_PARENT_INACTIVE");
        child.Status.Should().Be(CategoryStatus.Inactive);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task SetStatus_To_The_Current_Status_Must_Not_Reapply_The_Transition()
    {
        var fixture = new Fixture();
        var root = fixture.AddCategory("Root", "root");
        var child = fixture.AddCategory("Child", "child", root.Id);
        child.SetStatus(CategoryStatus.Active, parentIsActive: true,
            hasDependencies: false, TestTime.Now.AddMinutes(-1));
        child.ClearDomainEvents();

        var result = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(root.Id, CategoryStatus.Inactive));

        result.IsSuccess.Should().BeTrue();
        root.Status.Should().Be(CategoryStatus.Inactive);
        child.Status.Should().Be(CategoryStatus.Active);
        fixture.Categories.SubtreeLoadCount.Should().Be(0);
        fixture.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task SetStatus_Must_Reject_An_Undefined_Status_Before_IO()
    {
        var fixture = new Fixture();

        var result = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(1, (CategoryStatus)999));

        result.Error!.Code.Should().Be("CATEGORY_STATUS_INVALID");
        fixture.UnitOfWork.SaveCount.Should().Be(0);
        fixture.Categories.CallCount.Should().Be(0);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task SetStatus_Must_Persist_Successful_Active_And_Inactive_Transitions()
    {
        var fixture = new Fixture();
        var root = fixture.AddCategory("Root", "root");

        var activate = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(root.Id, CategoryStatus.Active));
        var deactivate = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(root.Id, CategoryStatus.Inactive));

        activate.IsSuccess.Should().BeTrue();
        deactivate.IsSuccess.Should().BeTrue();
        root.Status.Should().Be(CategoryStatus.Inactive);
        fixture.UnitOfWork.SaveCount.Should().Be(2);
    }

    [Fact]
    public async Task Deactivate_Must_Change_The_Whole_Live_Subtree_In_One_Save()
    {
        var fixture = new Fixture();
        var root = fixture.AddCategory("Root", "root", active: true);
        var child = fixture.AddCategory("Child", "child", root.Id, active: true);
        var deletedIntermediate = fixture.AddCategory(
            "Deleted intermediate", "deleted-intermediate", child.Id, active: true);
        deletedIntermediate.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, TestTime.Now.AddHours(-1));
        var grandchild = fixture.AddCategory(
            "Grandchild", "grandchild", deletedIntermediate.Id);
        grandchild.SetStatus(CategoryStatus.Active, parentIsActive: true,
            hasDependencies: false, TestTime.Now.AddMinutes(-1));
        var deletedAt = deletedIntermediate.DeletedAt;
        var deletedUpdatedAt = deletedIntermediate.UpdatedAt;

        var result = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(root.Id, CategoryStatus.Inactive));

        result.IsSuccess.Should().BeTrue();
        root.Status.Should().Be(CategoryStatus.Inactive);
        child.Status.Should().Be(CategoryStatus.Inactive);
        grandchild.Status.Should().Be(CategoryStatus.Inactive);
        root.UpdatedAt.Should().Be(TestTime.Now);
        child.UpdatedAt.Should().Be(TestTime.Now);
        grandchild.UpdatedAt.Should().Be(TestTime.Now);
        deletedIntermediate.Status.Should().Be(CategoryStatus.Deleted);
        deletedIntermediate.DeletedAt.Should().Be(deletedAt);
        deletedIntermediate.UpdatedAt.Should().Be(deletedUpdatedAt);
        fixture.UnitOfWork.SaveCount.Should().Be(1);
        fixture.Categories.SubtreeLoadCount.Should().Be(1);
    }

    [Fact]
    public async Task Bulk_deactivate_must_change_all_selected_subtrees_in_one_save()
    {
        var fixture = new Fixture();
        var firstRoot = fixture.AddCategory("First root", "first-root", active: true);
        var firstChild = fixture.AddCategory("First child", "first-child", firstRoot.Id, active: true);
        var secondRoot = fixture.AddCategory("Second root", "second-root", active: true);
        var secondChild = fixture.AddCategory("Second child", "second-child", secondRoot.Id, active: true);

        var result = await fixture.Sut.DeactivateManyAsync(
            new BulkDeactivateCategoriesCommand([firstRoot.Id, secondRoot.Id, firstRoot.Id]));

        result.IsSuccess.Should().BeTrue();
        new[] { firstRoot, firstChild, secondRoot, secondChild }
            .Should().OnlyContain(category => category.Status == CategoryStatus.Inactive);
        fixture.UnitOfWork.SaveCount.Should().Be(1);
        fixture.Categories.SubtreeLoadCount.Should().Be(1);
        fixture.Categories.LastSubtreeRootIds.Should().Equal(firstRoot.Id, secondRoot.Id);
    }

    [Fact]
    public async Task Bulk_deactivate_must_refuse_the_whole_request_when_a_selected_category_is_missing()
    {
        var fixture = new Fixture();
        var existing = fixture.AddCategory("Existing", "existing", active: true);

        var result = await fixture.Sut.DeactivateManyAsync(
            new BulkDeactivateCategoriesCommand([existing.Id, 999_999]));

        result.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
        existing.Status.Should().Be(CategoryStatus.Active);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
        fixture.Categories.SubtreeLoadCount.Should().Be(1);
    }

    [Fact]
    public async Task Deactivate_Must_Honor_Cancellation_After_The_Subtree_Query()
    {
        var fixture = new Fixture();
        var root = fixture.AddCategory("Root", "root", active: true);
        fixture.AddCategory("Child", "child", root.Id, active: true);
        using var cancellation = new CancellationTokenSource();
        fixture.Categories.AfterSubtreeLoad = cancellation.Cancel;

        var act = () => fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(root.Id, CategoryStatus.Inactive), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Categories.All.Should().OnlyContain(category => category.Status == CategoryStatus.Active);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
        fixture.Categories.LastToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task Deactivate_Must_Return_A_Save_Conflict_After_One_Save_Attempt()
    {
        var fixture = new Fixture();

        var root = fixture.AddCategory("Root", "root", active: true);

        fixture.UnitOfWork.NextSaveOutcome = SaveOutcome.ConcurrencyConflict;

        var result = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(root.Id, CategoryStatus.Inactive));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CONCURRENCY_CONFLICT");

        fixture.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Activate_Must_Not_Activate_Descendants()
    {
        var fixture = new Fixture();
        var root = fixture.AddCategory("Root", "root", active: true);
        var child = fixture.AddCategory("Child", "child", root.Id);
        var grandchild = fixture.AddCategory("Grandchild", "grandchild", child.Id);

        var result = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(child.Id, CategoryStatus.Active));

        result.IsSuccess.Should().BeTrue();
        child.Status.Should().Be(CategoryStatus.Active);
        grandchild.Status.Should().Be(CategoryStatus.Inactive);
    }

    [Fact]
    public async Task Create_Must_Reject_A_Normalized_Slug_Conflict()
    {
        var fixture = new Fixture();
        fixture.AddCategory("Existing", "summer-offers");
        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Duplicate", " SUMMER-OFFERS ", null, null, 0));

        result.Error!.Code.Should().Be("CATEGORY_SLUG_CONFLICT");
    }

    [Fact]
    public async Task Create_Must_Allow_A_Slug_Owned_Only_By_A_Deleted_Category()
    {
        var fixture = new Fixture();
        var deleted = fixture.AddCategory("Deleted", "reusable");
        deleted.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, TestTime.Now.AddHours(-1));

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Replacement", "REUSABLE", null, null, 0));

        result.IsSuccess.Should().BeTrue();
        fixture.Categories.All.Should().HaveCount(2);
    }

    [Fact]
    public async Task Create_Must_Reject_A_Case_Insensitive_Duplicate_Root_Name()
    {
        var fixture = new Fixture();
        fixture.AddCategory("Shoes", "shoes-existing");

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("  SHOES  ", "shoes-new", null, null, 0));

        result.Error!.Code.Should().Be("CATEGORY_NAME_CONFLICT");
        result.Error.Message.Should().Be("A sibling category already uses this name.");
        fixture.Categories.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Create_Must_Scope_Duplicate_Names_To_The_Selected_Parent()
    {
        var fixture = new Fixture();
        var firstParent = fixture.AddCategory("Men", "men");
        var secondParent = fixture.AddCategory("Women", "women");
        fixture.AddCategory("Shoes", "men-shoes", firstParent.Id);

        var duplicate = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("shoes", "men-shoes-new", null, firstParent.Id, 0));
        var otherLevel = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("shoes", "women-shoes", null, secondParent.Id, 0));

        duplicate.Error!.Code.Should().Be("CATEGORY_NAME_CONFLICT");
        otherLevel.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Must_Allow_A_Name_Owned_Only_By_A_Soft_Deleted_Sibling()
    {
        var fixture = new Fixture();
        var deleted = fixture.AddCategory("Shoes", "deleted-shoes");
        deleted.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, TestTime.Now.AddHours(-1));

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("SHOES", "replacement-shoes", null, null, 0));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Update_Must_Exclude_Current_Category_But_Reject_Another_Slug_Owner()
    {
        var fixture = new Fixture();
        var first = fixture.AddCategory("First", "first");
        fixture.AddCategory("Second", "second");

        var ownSlug = await fixture.Sut.UpdateAsync(
            new UpdateCategoryCommand(first.Id, "Renamed", " FIRST ", null, 4,
                CategoryStatus.Inactive));
        var otherSlug = await fixture.Sut.UpdateAsync(
            new UpdateCategoryCommand(first.Id, "Renamed", "SECOND", null, 4,
                CategoryStatus.Inactive));

        ownSlug.IsSuccess.Should().BeTrue();
        first.Name.Should().Be("Renamed");
        otherSlug.Error!.Code.Should().Be("CATEGORY_SLUG_CONFLICT");
        first.Slug.Value.Should().Be("first");
    }

    [Fact]
    public async Task Update_Must_Return_The_Persisted_Category_Details()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Original", "original", active: true);

        var result = await fixture.Sut.UpdateAsync(new UpdateCategoryCommand(
            category.Id, "Updated", "updated", "Updated description", 4,
            CategoryStatus.Active));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(new CategoryAdminDto(
            category.Id, null, "Updated", "updated", "Updated description", 4,
            CategoryStatus.Active));
    }

    [Fact]
    public async Task Update_Must_Change_Details_And_Status_In_One_Save()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Original", "original");

        var result = await fixture.Sut.UpdateAsync(new UpdateCategoryCommand(
            category.Id, "Updated", "updated", null, 4, CategoryStatus.Active));

        result.IsSuccess.Should().BeTrue();
        category.Name.Should().Be("Updated");
        category.Status.Should().Be(CategoryStatus.Active);
        fixture.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Update_Must_Not_Reapply_Unchanged_Active_Status_Under_An_Inactive_Parent()
    {
        var fixture = new Fixture();
        var inactiveParent = fixture.AddCategory("Inactive parent", "inactive-parent");
        var category = fixture.AddCategory("Original", "original", active: true);
        var move = await fixture.Sut.MoveAsync(
            new MoveCategoryCommand(category.Id, inactiveParent.Id, 0));

        var result = await fixture.Sut.UpdateAsync(new UpdateCategoryCommand(
            category.Id, "Renamed", "renamed", null, 4, CategoryStatus.Active));

        move.IsSuccess.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        category.Name.Should().Be("Renamed");
        category.Status.Should().Be(CategoryStatus.Active);
    }

    [Fact]
    public async Task Update_With_No_Status_Must_Leave_The_Current_Status_Unchanged()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Original", "original", active: true);

        var result = await fixture.Sut.UpdateAsync(new UpdateCategoryCommand(
            category.Id, "Renamed", "renamed", null, 4, null));

        result.IsSuccess.Should().BeTrue();
        category.Name.Should().Be("Renamed");
        category.Status.Should().Be(CategoryStatus.Active);
        fixture.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Update_To_Deleted_Must_Check_Dependencies_Before_Mutating_Details()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Original", "original");
        fixture.AddCategory("Child", "child", category.Id);

        var result = await fixture.Sut.UpdateAsync(new UpdateCategoryCommand(
            category.Id, "Changed", "changed", null, 4, CategoryStatus.Deleted));

        result.Error!.Code.Should().Be("CATEGORY_HAS_CHILDREN");
        category.Name.Should().Be("Original");
        category.Slug.Value.Should().Be("original");
        category.Status.Should().Be(CategoryStatus.Inactive);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task SetStatus_Deleted_Must_Reject_Product_Dependencies_Without_Mutating()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category", active: true);
        fixture.Assignments.Seed(CategoryProduct.Create(
            category.Id, ActiveProductId, TestTime.Now.AddHours(-1)));

        var result = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(category.Id, CategoryStatus.Deleted));

        result.Error!.Code.Should().Be("CATEGORY_HAS_CHILDREN");
        category.Status.Should().Be(CategoryStatus.Active);
        category.DeletedAt.Should().BeNull();
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Update_Must_Reject_A_Sibling_Name_But_Allow_The_Current_Name()
    {
        var fixture = new Fixture();
        var parent = fixture.AddCategory("Parent", "parent");
        var shoes = fixture.AddCategory("Shoes", "shoes", parent.Id);
        var boots = fixture.AddCategory("Boots", "boots", parent.Id);

        var ownName = await fixture.Sut.UpdateAsync(
            new UpdateCategoryCommand(shoes.Id, "  SHOES  ", "shoes", null, 0,
                CategoryStatus.Inactive));
        var siblingName = await fixture.Sut.UpdateAsync(
            new UpdateCategoryCommand(boots.Id, "shoes", "boots", null, 0,
                CategoryStatus.Inactive));

        ownName.IsSuccess.Should().BeTrue();
        siblingName.Error!.Code.Should().Be("CATEGORY_NAME_CONFLICT");
        siblingName.Error.Message.Should().Be("A sibling category already uses this name.");
        boots.Name.Should().Be("Boots");
    }

    [Fact]
    public async Task Update_Malformed_Details_Must_Fail_Before_Repository_Or_UnitOfWork_IO()
    {
        var fixture = new Fixture();

        var result = await fixture.Sut.UpdateAsync(
            new UpdateCategoryCommand(1, "", "valid-slug", null, 0,
                CategoryStatus.Inactive));

        result.Error!.Code.Should().Be("CATEGORY_NAME_REQUIRED");
        result.Error.Message.Should().Be("A category name is required.");
        fixture.Categories.CallCount.Should().Be(0);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_Unsupported_Name_Must_Fail_Before_Repository_Or_UnitOfWork_IO()
    {
        var fixture = new Fixture();

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Shoes 😀", "shoes", null, null, 0));

        result.Error!.Code.Should().Be("CATEGORY_NAME_UNSUPPORTED");
        result.Error.Message.Should().Be("A category name can contain letters, digits, spaces and basic punctuation.");
        fixture.Categories.CallCount.Should().Be(0);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Move_Must_Reject_A_Descendant_As_The_New_Parent()
    {
        var fixture = new Fixture();
        var root = fixture.AddCategory("Root", "root", active: true);
        var child = fixture.AddCategory("Child", "child", root.Id, active: true);
        var grandchild = fixture.AddCategory("Grandchild", "grandchild", child.Id, active: true);
        var result = await fixture.Sut.MoveAsync(new MoveCategoryCommand(root.Id, grandchild.Id, 2));

        result.Error!.Code.Should().Be("CATEGORY_CYCLE");
        root.ParentId.Should().BeNull();
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Move_Must_Allow_An_Inactive_New_Parent()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category", active: true);
        var parent = fixture.AddCategory("Parent", "parent");
        var result = await fixture.Sut.MoveAsync(new MoveCategoryCommand(category.Id, parent.Id, 2));

        result.IsSuccess.Should().BeTrue();
        category.ParentId.Should().Be(parent.Id);
        fixture.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Move_Must_Reject_When_A_Descendant_Would_End_Below_Level_Five()
    {
        var fixture = new Fixture();
        var levelOne = fixture.AddCategory("Level 1", "level-1");
        var levelTwo = fixture.AddCategory("Level 2", "level-2", levelOne.Id);
        var levelThree = fixture.AddCategory("Level 3", "level-3", levelTwo.Id);
        var levelFour = fixture.AddCategory("Level 4", "level-4", levelThree.Id);
        var movingRoot = fixture.AddCategory("Moving", "moving");
        fixture.AddCategory("Moving child", "moving-child", movingRoot.Id);

        var result = await fixture.Sut.MoveAsync(
            new MoveCategoryCommand(movingRoot.Id, levelFour.Id, 0));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_DEPTH_EXCEEDED");
        result.Error.Message.Should().Be("A category tree cannot be deeper than five levels.");
        movingRoot.ParentId.Should().BeNull();
    }

    [Fact]
    public async Task Create_Must_Stop_Traversal_When_Cancellation_Is_Requested()
    {
        var fixture = new Fixture();
        var parent = fixture.AddCategory("Parent", "parent");
        using var cancellation = new CancellationTokenSource();
        fixture.Categories.AfterHierarchyLoad = cancellation.Cancel;

        var act = () => fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Child", "child", null, parent.Id, 0),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        fixture.Categories.LastToken.Should().Be(cancellation.Token);
        fixture.Categories.All.Should().ContainSingle();
    }

    [Fact]
    public async Task Move_Malformed_Details_Must_Fail_Before_Repository_Or_UnitOfWork_IO()
    {
        var fixture = new Fixture();

        var result = await fixture.Sut.MoveAsync(
            new MoveCategoryCommand(1, null, -1));

        result.Error!.Code.Should().Be("CATEGORY_DISPLAY_ORDER_INVALID");
        fixture.Categories.CallCount.Should().Be(0);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Move_Self_Parent_Must_Fail_Before_Repository_Or_UnitOfWork_IO()
    {
        var fixture = new Fixture();

        var result = await fixture.Sut.MoveAsync(
            new MoveCategoryCommand(1, 1, 0));

        result.Error!.Code.Should().Be("CATEGORY_PARENT_INVALID");
        fixture.Categories.CallCount.Should().Be(0);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_Must_Reject_A_Non_Leaf_And_Delete_A_Leaf()
    {
        var fixture = new Fixture();

        var parent = fixture.AddCategory("Parent", "parent");
        var leaf = fixture.AddCategory("Leaf", "leaf", parent.Id);

        var parentResult = await fixture.Sut.DeleteAsync(
            new DeleteCategoryCommand(parent.Id));

        var leafResult = await fixture.Sut.DeleteAsync(
            new DeleteCategoryCommand(leaf.Id));

        parentResult.Error!.Code.Should().Be("CATEGORY_HAS_CHILDREN");

        parentResult.Error.Message.Should().Be(
            "امکان حذف دسته‌بندی دارای کالا یا زیردسته وجود ندارد. ابتدا وابستگی‌ها را منتقل کنید");

        parent.Status.Should().NotBe(CategoryStatus.Deleted);

        leafResult.IsSuccess.Should().BeTrue();
        leaf.DeletedAt.Should().Be(TestTime.Now);

        fixture.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Delete_Must_Reject_A_Category_With_A_Linked_Product()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category");
        fixture.Assignments.Seed(CategoryProduct.Create(
            category.Id, ActiveProductId, TestTime.Now.AddHours(-1)));

        var result = await fixture.Sut.DeleteAsync(new DeleteCategoryCommand(category.Id));

        result.Error!.Code.Should().Be("CATEGORY_HAS_CHILDREN");
        result.Error.Message.Should().Be(
            "امکان حذف دسته‌بندی دارای کالا یا زیردسته وجود ندارد. ابتدا وابستگی‌ها را منتقل کنید");
        category.Status.Should().NotBe(CategoryStatus.Deleted);
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Delete_Must_Forward_Cancellation_To_Dependency_Checks()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category");
        using var cancellation = new CancellationTokenSource();

        await fixture.Sut.DeleteAsync(
            new DeleteCategoryCommand(category.Id), cancellation.Token);

        fixture.Categories.LastToken.Should().Be(cancellation.Token);
        fixture.Assignments.LastToken.Should().Be(cancellation.Token);
    }

    [Fact]
    public async Task Mutations_Must_Return_A_Result_Failure_For_A_Deleted_Category()
    {
        var fixture = new Fixture();
        var deleted = fixture.AddCategory("Deleted", "deleted");
        deleted.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, TestTime.Now.AddMinutes(-1));

        var update = await fixture.Sut.UpdateAsync(
            new UpdateCategoryCommand(deleted.Id, "Changed", "changed", null, 0,
                CategoryStatus.Deleted));
        var move = await fixture.Sut.MoveAsync(new MoveCategoryCommand(deleted.Id, null, 0));
        var activate = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(deleted.Id, CategoryStatus.Active));
        var deactivate = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(deleted.Id, CategoryStatus.Inactive));
        var delete = await fixture.Sut.DeleteAsync(new DeleteCategoryCommand(deleted.Id));

        new[] { update, move, activate, deactivate, delete }.Should().OnlyContain(result =>
            result.IsFailure && result.Error!.Code == "CATEGORY_DELETED");
        fixture.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Assign_Must_Reject_Deleted_Categories_And_Invalid_Catalog_Products()
    {
        var fixture = new Fixture();
        var deleted = fixture.AddCategory("Deleted", "deleted");
        deleted.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, TestTime.Now.AddMinutes(-1));
        var active = fixture.AddCategory("Active", "active");

        var deletedResult = await fixture.Sut.AssignProductAsync(
            new AssignCategoryProductCommand(deleted.Id, ActiveProductId, null));
        var productResult = await fixture.Sut.AssignProductAsync(
            new AssignCategoryProductCommand(active.Id, MissingProductId, null));

        deletedResult.Error!.Code.Should().Be("CATEGORY_DELETED");
        productResult.Error!.Code.Should().Be("CATEGORY_PRODUCT_INVALID");
        fixture.Assignments.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_Assignment_And_Missing_Unassignment_Must_Be_Idempotent()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category");
        fixture.Assignments.Seed(CategoryProduct.Create(category.Id, ActiveProductId, TestTime.Now));

        var assign = await fixture.Sut.AssignProductAsync(
            new AssignCategoryProductCommand(category.Id, ActiveProductId, Guid.NewGuid()));
        var unassign = await fixture.Sut.UnassignProductAsync(
            new UnassignCategoryProductCommand(category.Id, MissingProductId));

        assign.IsSuccess.Should().BeTrue();
        unassign.IsSuccess.Should().BeTrue();
        fixture.Assignments.All.Should().ContainSingle();
        fixture.Assignments.MutationCount.Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_Assignment_Must_Succeed_Without_Catalog_Lookup_After_Product_Deactivation()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category");
        fixture.Assignments.Seed(CategoryProduct.Create(category.Id, ActiveProductId, TestTime.Now));
        fixture.Catalog.Deactivate(ActiveProductId);

        var result = await fixture.Sut.AssignProductAsync(
            new AssignCategoryProductCommand(category.Id, ActiveProductId, null));

        result.IsSuccess.Should().BeTrue();
        fixture.Catalog.CallCount.Should().Be(0);
        fixture.Assignments.MutationCount.Should().Be(0);
    }

    [Fact]
    public async Task Assign_And_Unassign_Must_Persist_The_Association()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category");
        var actorId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var assign = await fixture.Sut.AssignProductAsync(
            new AssignCategoryProductCommand(category.Id, ActiveProductId, actorId));

        assign.IsSuccess.Should().BeTrue();
        var association = fixture.Assignments.All.Should().ContainSingle().Which;
        association.AssignedAt.Should().Be(TestTime.Now);
        association.AssignedByUserId.Should().Be(actorId);
        var unassign = await fixture.Sut.UnassignProductAsync(
            new UnassignCategoryProductCommand(category.Id, ActiveProductId));
        unassign.IsSuccess.Should().BeTrue();
        fixture.Assignments.All.Should().BeEmpty();
        fixture.Assignments.MutationCount.Should().Be(2);
    }

    [Fact]
    public async Task Assign_Must_Check_And_Write_Inside_One_Serialized_Transaction()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category");

        var result = await fixture.Sut.AssignProductAsync(
            new AssignCategoryProductCommand(category.Id, ActiveProductId, null));

        result.IsSuccess.Should().BeTrue();
        fixture.UnitOfWork.SaveCount.Should().Be(1);
        fixture.Log.Should().Equal(
            "category.get",
            "assignment.exists",
            "catalog.lookup",
            "assignment.add",
            "save");
    }

    [Fact]
    public async Task Invalid_Parent_Must_Fail_Before_Transaction_Or_Repository_IO()
    {
        var fixture = new Fixture();
        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Child", "child", null, -1, 0));

        result.Error!.Code.Should().Be("CATEGORY_PARENT_INVALID");
        fixture.UnitOfWork.SaveCount.Should().Be(0);
        fixture.Categories.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Empty_Product_Id_Must_Fail_Before_Repository_Or_Catalog_IO()
    {
        var fixture = new Fixture();
        var result = await fixture.Sut.AssignProductAsync(
            new AssignCategoryProductCommand(1, 0, null));

        result.Error!.Code.Should().Be("CATEGORY_PRODUCT_INVALID");
        fixture.Categories.CallCount.Should().Be(0);
        fixture.Assignments.CallCount.Should().Be(0);
        fixture.Catalog.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Create_Must_Commit_The_Serialized_UnitOfWork_Transaction()
    {
        var fixture = new Fixture();

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Root", "root", null, null, 0));

        result.IsSuccess.Should().BeTrue();
        fixture.UnitOfWork.SaveCount.Should().Be(1);
        fixture.Log.Should().Equal("save");
    }

    // The three writes whose validity depends on the parent's state must also touch the parent,
    // so a concurrent writer that read the old parent loses on its concurrency token. This is what
    // replaced the serializable transaction (ADR-0017 D8); without it both writes commit.

    [Fact]
    public async Task Create_Touches_The_Parent()
    {
        var fixture = new Fixture();
        var parent = fixture.AddCategory("Parent", "parent", active: true);

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Child", "child", null, parent.Id, 0));

        result.IsSuccess.Should().BeTrue();
        parent.UpdatedAt.Should().Be(TestTime.Now);
    }

    [Fact]
    public async Task Move_Touches_The_New_Parent()
    {
        var fixture = new Fixture();
        var newParent = fixture.AddCategory("New parent", "new-parent", active: true);
        var moved = fixture.AddCategory("Moved", "moved");

        var result = await fixture.Sut.MoveAsync(
            new MoveCategoryCommand(moved.Id, newParent.Id, 0));

        result.IsSuccess.Should().BeTrue();
        newParent.UpdatedAt.Should().Be(TestTime.Now);
    }

    [Fact]
    public async Task Activation_Touches_The_Parent()
    {
        var fixture = new Fixture();
        var parent = fixture.AddCategory("Parent", "parent", active: true);
        var child = fixture.AddCategory("Child", "child", parent.Id);

        var result = await fixture.Sut.SetStatusAsync(
            new SetCategoryStatusCommand(child.Id, CategoryStatus.Active));

        result.IsSuccess.Should().BeTrue();
        parent.UpdatedAt.Should().Be(TestTime.Now);
    }

    [Theory]
    [InlineData(SaveOutcome.ConcurrencyConflict, "CONCURRENCY_CONFLICT")]
    [InlineData(SaveOutcome.UniqueViolation, "CONFLICT")]
    public async Task Lost_Save_Races_Must_Be_Translated_To_Result_Failures(
        SaveOutcome outcome, string expectedCode)
    {
        var fixture = new Fixture();
        fixture.UnitOfWork.NextSaveOutcome = outcome;

        var result = await fixture.Sut.CreateAsync(
            new CreateCategoryCommand(
                "Root",
                "root",
                null,
                null,
                0));

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be(expectedCode);

        fixture.Log.Should().NotContain("dispatch");
        fixture.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_Must_Propagate_Cancellation_To_All_Boundaries()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        await fixture.Sut.CreateAsync(
            new CreateCategoryCommand("Root", "root", null, null, 0), token);

        fixture.UnitOfWork.LastToken.Should().Be(token);
        fixture.Categories.LastToken.Should().Be(token);
        //fixture.Events.LastToken.Should().Be(token);
    }

    [Fact]
    public async Task Assign_Product_Must_Propagate_Cancellation_To_All_Boundaries()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category");
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        await fixture.Sut.AssignProductAsync(
            new AssignCategoryProductCommand(category.Id, ActiveProductId, null), token);

        fixture.Categories.LastToken.Should().Be(token);
        fixture.Catalog.LastToken.Should().Be(token);
        fixture.Assignments.LastToken.Should().Be(token);
    }

    [Fact]
    public void Dependency_Injection_Must_Resolve_The_Inbound_Service()
    {
        var services = new ServiceCollection();
        var log = new List<string>();

        services.AddSingleton<ICategoryRepository>(
            new InMemoryCategoryRepository(log));

        services.AddSingleton<ICategoryProductRepository>(
            new InMemoryCategoryProductRepository(log));

        services.AddSingleton<IUnitOfWork>(
            new InMemoryCategoryUnitOfWork(log));



        services.AddSingleton<ICatalogProductLookup>(
            new StubCatalogProductLookup(ActiveProductId, log));

        services.AddCategoryApplication();

        var descriptor = services.Single(item =>
            item.ServiceType == typeof(ICategoryService));

        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
        descriptor.ImplementationType.Should().Be<CategoryService>();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetRequiredService<ICategoryService>();

        resolved.Should().BeOfType<CategoryService>();
    }

    [Fact]
    public async Task Atomic_Association_Race_Outcomes_Must_Be_Idempotent()
    {
        var fixture = new Fixture();
        var category = fixture.AddCategory("Category", "category");
        fixture.Assignments.ForcedAddOutcome = AssociationMutationOutcome.AlreadyInDesiredState;
        var assign = await fixture.Sut.AssignProductAsync(
            new AssignCategoryProductCommand(category.Id, ActiveProductId, null));

        fixture.Assignments.ForcedRemoveOutcome = AssociationMutationOutcome.AlreadyInDesiredState;
        var unassign = await fixture.Sut.UnassignProductAsync(
            new UnassignCategoryProductCommand(category.Id, ActiveProductId));
        assign.IsSuccess.Should().BeTrue();
        unassign.IsSuccess.Should().BeTrue();
        fixture.Assignments.MutationCount.Should().Be(2);
    }

    private sealed class Fixture
    {
        public List<string> Log { get; } = [];
        public InMemoryCategoryRepository Categories { get; }
        public InMemoryCategoryProductRepository Assignments { get; }
        public InMemoryCategoryUnitOfWork UnitOfWork { get; }
        public StubCatalogProductLookup Catalog { get; }
        public SequentialIdGenerator IdGenerator { get; }
        //public RecordingEventDispatcher Events { get; }
        public CategoryService Sut { get; }

        public Fixture()
        {
            Categories = new InMemoryCategoryRepository(Log);
            Assignments = new InMemoryCategoryProductRepository(Log);
            UnitOfWork = new InMemoryCategoryUnitOfWork(Log);
            Catalog = new StubCatalogProductLookup(ActiveProductId, Log);
            IdGenerator = new SequentialIdGenerator();
            //Events = new RecordingEventDispatcher(Log);
            Sut = new CategoryService(
                Categories, Assignments, UnitOfWork, Catalog, /*Events,*/ new FixedClock(),
                IdGenerator);
        }

        public Category AddCategory(string name, string slug, long? parentId = null, bool active = false)
        {
            var category = Category.Create(
                IdGenerator.Generate(), name, slug, null, parentId, 0, TestTime.Now.AddDays(-1));
            Categories.Seed(category);
            if (active)
                category.SetStatus(CategoryStatus.Active,
                    parentId is null || Categories.All.Single(x => x.Id == parentId).Status == CategoryStatus.Active,
                    hasDependencies: false, TestTime.Now.AddHours(-1));
            category.ClearDomainEvents();
            return category;
        }
    }

    private sealed class InMemoryCategoryRepository : ICategoryRepository
    {
        private readonly Dictionary<long, Category> _categories = [];
        private readonly List<string>? _log;
        private long _nextId = 1;

        public InMemoryCategoryRepository(List<string>? log = null) => _log = log;

        public IReadOnlyCollection<Category> All => _categories.Values;
        public int CallCount { get; private set; }
        public int HierarchyLoadCount { get; private set; }
        public int SubtreeLoadCount { get; private set; }
        public IReadOnlyList<long> LastSubtreeRootIds { get; private set; } = [];
        public int GetByIdCount { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Action? AfterHierarchyLoad { get; set; }
        public Action? AfterSubtreeLoad { get; set; }

        public void Seed(Category category)
        {
            if (category.Id == 0)
                typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, _nextId++);
            _categories.Add(category.Id, category);
        }

        public Task<Category?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            _log?.Add("category.get");
            CallCount++;
            GetByIdCount++;
            LastToken = ct;
            return Task.FromResult(_categories.GetValueOrDefault(id));
        }

        public Task<IReadOnlyList<Category>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct = default)
        {
            CallCount++;
            LastToken = ct;
            IReadOnlyList<Category> matches = _categories.Values.Where(category => ids.Contains(category.Id)).ToArray();
            return Task.FromResult(matches);
        }

        public Task<bool> NameExistsAtLevelAsync(string normalizedName, long? parentId,
            long? excludingCategoryId = null, CancellationToken ct = default)
        {
            CallCount++;
            LastToken = ct;
            return Task.FromResult(_categories.Values.Any(category =>
                category.Status != CategoryStatus.Deleted
                && category.ParentId == parentId
                && category.Id != excludingCategoryId
                && category.NormalizedName == normalizedName));
        }

        public Task<bool> SlugExistsAsync(string slug, long? excludingCategoryId = null,
            CancellationToken ct = default)
        {
            CallCount++;
            LastToken = ct;
            return Task.FromResult(_categories.Values.Any(category => category.Status != CategoryStatus.Deleted &&
                category.Id != excludingCategoryId && category.Slug.Value == slug));
        }

        public Task<Category?> FindByCanonicalPathAsync(
            IReadOnlyList<string> canonicalKeyPath, CancellationToken ct = default)
        {
            CallCount++;
            LastToken = ct;

            Category? current = null;
            foreach (var step in canonicalKeyPath ?? [])
            {
                var parentId = current?.Id;
                current = _categories.Values.FirstOrDefault(category =>
                    category.ParentId == parentId
                    && category.Status != CategoryStatus.Deleted
                    && (category.CanonicalKey == step || category.Aliases.Contains(step)));

                if (current is null) return Task.FromResult<Category?>(null);
            }

            return Task.FromResult(current);
        }

        public Task<bool> HasChildrenAsync(long categoryId, CancellationToken ct = default)
        {
            CallCount++;
            LastToken = ct;
            return Task.FromResult(_categories.Values.Any(category =>
                category.Status != CategoryStatus.Deleted && category.ParentId == categoryId));
        }

        public Task<IReadOnlyList<CategoryHierarchyNode>> GetHierarchyAsync(
            long? ancestorId, long? subtreeRootId, CancellationToken ct = default)
        {
            CallCount++;
            HierarchyLoadCount++;
            LastToken = ct;
            IReadOnlyList<CategoryHierarchyNode> hierarchy = _categories.Values
                .Select(category => new CategoryHierarchyNode(category.Id, category.ParentId))
                .ToArray();
            AfterHierarchyLoad?.Invoke();
            return Task.FromResult(hierarchy);
        }

        public Task<IReadOnlyList<Category>> GetSubtreeForUpdateAsync(
            long rootId, CancellationToken ct = default)
            => GetSubtreesForUpdateAsync([rootId], ct);

        public Task<IReadOnlyList<Category>> GetSubtreesForUpdateAsync(
            IReadOnlyCollection<long> rootIds, CancellationToken ct = default)
        {
            CallCount++;
            SubtreeLoadCount++;
            LastToken = ct;
            LastSubtreeRootIds = rootIds.Distinct().ToArray();
            var children = _categories.Values.ToLookup(category => category.ParentId);
            var visited = new HashSet<long>();
            var pending = new Queue<long>();
            var subtree = new List<Category>();
            foreach (var rootId in LastSubtreeRootIds) pending.Enqueue(rootId);
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var id = pending.Dequeue();
                if (!visited.Add(id) || !_categories.TryGetValue(id, out var category)) continue;
                subtree.Add(category);
                foreach (var child in children[id]) pending.Enqueue(child.Id);
            }
            AfterSubtreeLoad?.Invoke();
            return Task.FromResult<IReadOnlyList<Category>>(subtree);
        }

        public Task AddAsync(Category category, CancellationToken ct = default)
        {
            LastToken = ct;
            Seed(category);
            return Task.CompletedTask;
        }

    }

    private sealed class InMemoryCategoryProductRepository : ICategoryProductRepository
    {
        private readonly Dictionary<(long, long), CategoryProduct> _assignments = [];
        private readonly List<string>? _log;
        public IReadOnlyCollection<CategoryProduct> All => _assignments.Values;
        public int MutationCount { get; private set; }
        public int CallCount { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public AssociationMutationOutcome? ForcedAddOutcome { get; set; }
        public AssociationMutationOutcome? ForcedRemoveOutcome { get; set; }
        public InMemoryCategoryProductRepository(List<string>? log = null) => _log = log;
        public void Seed(CategoryProduct assignment) =>
            _assignments.Add((assignment.CategoryId, assignment.ProductId), assignment);

        public Task<bool> ExistsAsync(long categoryId, long productId, CancellationToken ct = default)
        {
            _log?.Add("assignment.exists");
            CallCount++;
            LastToken = ct;
            return Task.FromResult(_assignments.ContainsKey((categoryId, productId)));
        }

        public Task<bool> HasAnyForCategoryAsync(
            long categoryId, CancellationToken ct = default)
        {
            _log?.Add("assignment.has-any");
            CallCount++;
            LastToken = ct;
            return Task.FromResult(_assignments.Keys.Any(key => key.Item1 == categoryId));
        }

        public Task<IReadOnlyList<long>> ListCategoryIdsForProductAsync(
            long productId, CancellationToken ct = default)
        {
            _log?.Add("assignment.list-for-product");
            CallCount++;
            LastToken = ct;
            return Task.FromResult<IReadOnlyList<long>>(
                _assignments.Keys.Where(key => key.Item2 == productId).Select(key => key.Item1).ToList());
        }

        public Task<AssociationMutationOutcome> AddIfAbsentAsync(
            CategoryProduct assignment, CancellationToken ct = default)
        {
            _log?.Add("assignment.add");
            CallCount++;
            LastToken = ct;
            MutationCount++;
            if (ForcedAddOutcome.HasValue) return Task.FromResult(ForcedAddOutcome.Value);
            if (_assignments.ContainsKey((assignment.CategoryId, assignment.ProductId)))
                return Task.FromResult(AssociationMutationOutcome.AlreadyInDesiredState);
            Seed(assignment);
            return Task.FromResult(AssociationMutationOutcome.Applied);
        }

        public Task<AssociationMutationOutcome> RemoveIfPresentAsync(
            long categoryId, long productId, CancellationToken ct = default)
        {
            CallCount++;
            LastToken = ct;
            MutationCount++;
            if (ForcedRemoveOutcome.HasValue) return Task.FromResult(ForcedRemoveOutcome.Value);
            return Task.FromResult(_assignments.Remove((categoryId, productId))
                ? AssociationMutationOutcome.Applied
                : AssociationMutationOutcome.AlreadyInDesiredState);
        }

        public Task AddAsync(CategoryProduct assignment,CancellationToken ct = default)
        {
            _log?.Add("assignment.add");
            CallCount++;
            MutationCount++;
            LastToken = ct;

            Seed(assignment);

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryCategoryUnitOfWork : IUnitOfWork
    {
        private readonly List<string> _log;
        public int SaveCount { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Error NextSaveError { get; set; } = Error.None;
        public SaveOutcome NextSaveOutcome { get; set; } = SaveOutcome.Saved;

        public InMemoryCategoryUnitOfWork(List<string> log) => _log = log;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            LastToken = ct;
            _log.Add("save");

            if (NextSaveError != Error.None)
                throw new PersistenceTestException(NextSaveError);

            return Task.FromResult(1);
        }

        public int SaveChanges()
        {
            SaveCount++;
            _log.Add("save");

            if (NextSaveError != Error.None)
                throw new PersistenceTestException(NextSaveError);

            return 1;
        }

        public Task<SaveOutcome> TrySaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            LastToken = ct;
            _log.Add("save");
            return Task.FromResult(NextSaveOutcome);
        }

    }


    //private sealed class RecordingEventDispatcher(List<string> log) : IEventDispatcher
    //{
    //    public CancellationToken LastToken { get; private set; }
    //    public Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
    //        where TEvent : IDomainEvent
    //    {
    //        LastToken = ct;
    //        log.Add("dispatch");
    //        return Task.CompletedTask;
    //    }

    //    public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken ct = default)
    //    {
    //        LastToken = ct;
    //        log.Add("dispatch");
    //        return Task.CompletedTask;
    //    }
    //}

    private sealed class StubCatalogProductLookup : ICatalogProductLookup
    {
        private readonly HashSet<long> _activeProductIds;
        private readonly List<string>? _log;
        public StubCatalogProductLookup(long productId, List<string>? log = null)
        {
            _activeProductIds = [productId];
            _log = log;
        }
        public int CallCount { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public void Deactivate(long productId) => _activeProductIds.Remove(productId);
        public Task<bool> ExistsAndIsActiveAsync(long productId, CancellationToken ct = default)
        {
            _log?.Add("catalog.lookup");
            CallCount++;
            LastToken = ct;
            return Task.FromResult(_activeProductIds.Contains(productId));
        }
    }

    private sealed class FixedIdGenerator(long id) : IIdGenerator
    {
        public long Generate() => id;
    }

    private sealed class SequentialIdGenerator : IIdGenerator
    {
        private long _nextId = 100;
        public long Generate() => Interlocked.Increment(ref _nextId);
    }

    private sealed class PersistenceTestException(Error error)
        : Exception(error.Message)
    {
        public Error Error { get; } = error;
    }
}


