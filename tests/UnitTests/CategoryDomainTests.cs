using AwesomeAssertions;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class CategoryDomainTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 31, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ChangedAt = CreatedAt.AddHours(1);
    private const long GeneratedCategoryId = 912345678901L;
    private static long _nextCategoryId = 100;

    [Fact]
    public void Create_Must_Preserve_The_Application_Generated_Id()
    {
        var create = typeof(Category).GetMethod(
            nameof(Category.Create),
            [typeof(long), typeof(string), typeof(string), typeof(string), typeof(long?),
                typeof(int), typeof(DateTimeOffset)]);

        create.Should().NotBeNull();
        var category = (Category)create!.Invoke(null,
            [GeneratedCategoryId, "Category", "category", null, null, 0, CreatedAt])!;

        category.Id.Should().Be(GeneratedCategoryId);
    }

    [Fact]
    public void Create_Must_Trim_Details_Normalize_Slug_And_Initialize_Lifecycle_State()
    {
        var category = Category.Create(
            GeneratedCategoryId, "  Summer Offers  ", "  SuMmEr-OfFeRs  ",
            "  Seasonal picks  ", 41, 3, CreatedAt);

        category.ParentId.Should().Be(41);
        category.Name.Should().Be("Summer Offers");
        category.Slug.Value.Should().Be("summer-offers");
        category.Description.Should().Be("Seasonal picks");
        category.DisplayOrder.Should().Be(3);
        category.Status.Should().Be(CategoryStatus.Inactive);
        category.DeletedAt.Should().BeNull();
        category.CreatedAt.Should().Be(CreatedAt);
        category.UpdatedAt.Should().BeNull();
        category.RowVersion.Should().Be(0u);
        category.DomainEvents.Should().ContainSingle().Which
            .Should().BeOfType<CategoryCreatedDomainEvent>();
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Must_Reject_A_Missing_Name(string name)
    {
        var act = () => CreateCategory(name: name);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("CATEGORY_NAME_REQUIRED");
        error.Message.Should().Be("A category name is required.");
    }

    [Fact]
    public void Create_Must_Allow_A_Name_With_Exactly_60_Trimmed_Characters()
    {
        var category = CreateCategory(name: $" {new string('n', 60)} ");

        category.Name.Should().HaveLength(60);
    }

    [Fact]
    public void Create_Must_Reject_A_Name_Longer_Than_60_Trimmed_Characters()
    {
        var act = () => CreateCategory(name: $" {new string('n', 61)} ");

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("CATEGORY_NAME_TOO_LONG");
        error.Message.Should().Be("A category name cannot exceed 60 characters.");
    }

    [Fact]
    public void Create_Must_Allow_The_Approved_Category_Name_Characters()
    {
        var category = CreateCategory(name: "کفش‌های مردانه أحذية Shoes_2026-ویژه");

        category.Name.Should().Be("کفش‌های مردانه أحذية Shoes_2026-ویژه");
    }

    [Theory]
    [InlineData("Shoes 😀")]
    [InlineData("Shoes ❤️")]
    [InlineData("Shoes ©")]
    [InlineData("Shoes/Boots")]
    public void Create_Must_Reject_Unsupported_Category_Name_Characters(string name)
    {
        var act = () => CreateCategory(name: name);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("CATEGORY_NAME_UNSUPPORTED");
        error.Message.Should().Be("A category name can contain letters, digits, spaces and basic punctuation.");
    }

    [Fact]
    public void Update_Must_Reject_Unsupported_Category_Name_Characters()
    {
        var category = CreateCategory();

        var act = () => category.Update("Shoes 😀", "shoes", null, 0, ChangedAt);

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_NAME_UNSUPPORTED");
    }
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Slug_Must_Reject_A_Missing_Value(string slug)
    {
        var act = () => CategorySlug.Create(slug);

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_SLUG_REQUIRED");
    }

    [Fact]
    public void Slug_Must_Reject_A_Value_Longer_Than_200_Normalized_Characters()
    {
        var act = () => CategorySlug.Create($" {new string('s', 201)} ");

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_SLUG_TOO_LONG");
    }
    [Fact]
    public void Create_Must_Reject_A_Description_Longer_Than_1000_Trimmed_Characters()
    {
        var act = () => CreateCategory(description: $" {new string('d', 1001)} ");

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_DESCRIPTION_TOO_LONG");
    }

    [Fact]
    public void Create_Must_Reject_A_Negative_Display_Order()
    {
        var act = () => CreateCategory(displayOrder: -1);

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_DISPLAY_ORDER_INVALID");
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_Must_Reject_A_Non_Positive_Parent(long parentId)
    {
        var act = () => CreateCategory(parentId: parentId);

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_PARENT_INVALID");
    }
    [Fact]
    public void Update_Must_Replace_Validated_Details_And_Record_The_Change()
    {
        var category = CreateCategory();

        category.Update(" Updated ", " UPDATED-SLUG ", " Updated description ", 7, ChangedAt);

        category.Name.Should().Be("Updated");
        category.Slug.Value.Should().Be("updated-slug");
        category.Description.Should().Be("Updated description");
        category.DisplayOrder.Should().Be(7);
        category.UpdatedAt.Should().Be(ChangedAt);
        category.DomainEvents.Should().Contain(e => e is CategoryUpdatedDomainEvent);
    }
    [Fact]
    public void Move_Must_Reject_The_Category_As_Its_Own_Parent()
    {
        var category = CreateCategory();
        SetPersistedId(category, 73);

        var act = () => category.Move(73, 0, ChangedAt);

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_PARENT_INVALID");
    }

    [Fact]
    public void Move_Must_Change_Parent_And_Sibling_Order()
    {
        var category = CreateCategory(parentId: 12);
        SetPersistedId(category, 73);

        category.Move(25, 9, ChangedAt);

        category.ParentId.Should().Be(25);
        category.DisplayOrder.Should().Be(9);
        category.UpdatedAt.Should().Be(ChangedAt);
        category.DomainEvents.Should().Contain(e => e is CategoryMovedDomainEvent);
    }
    [Fact]
    public void SetStatus_Active_Must_Reject_A_Child_Whose_Parent_Is_Not_Active()
    {
        var category = CreateCategory(parentId: 12);

        var act = () => category.SetStatus(
            CategoryStatus.Active, parentIsActive: false, hasDependencies: false, ChangedAt);

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_PARENT_INACTIVE");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(12L, true)]
    public void SetStatus_Active_Must_Enable_A_Root_Or_A_Child_With_An_Active_Parent(
        long? parentId, bool parentIsActive)
    {
        var category = CreateCategory(parentId: parentId);

        category.SetStatus(CategoryStatus.Active, parentIsActive, hasDependencies: false, ChangedAt);

        category.Status.Should().Be(CategoryStatus.Active);
        category.UpdatedAt.Should().Be(ChangedAt);
        category.DomainEvents.Should().Contain(e => e is CategoryActivatedDomainEvent);
    }
    [Fact]
    public void SetStatus_Inactive_Must_Disable_An_Active_Category()
    {
        var category = CreateCategory();
        category.SetStatus(CategoryStatus.Active, parentIsActive: false,
            hasDependencies: false, CreatedAt.AddMinutes(30));
        category.ClearDomainEvents();

        category.SetStatus(CategoryStatus.Inactive, parentIsActive: false,
            hasDependencies: false, ChangedAt);

        category.Status.Should().Be(CategoryStatus.Inactive);
        category.UpdatedAt.Should().Be(ChangedAt);
        category.DomainEvents.Should().ContainSingle().Which
            .Should().BeOfType<CategoryDeactivatedDomainEvent>();
    }
    [Fact]
    public void SetStatus_Deleted_Must_Reject_A_Category_With_Dependencies()
    {
        var category = CreateCategory();

        var act = () => category.SetStatus(
            CategoryStatus.Deleted, parentIsActive: false, hasDependencies: true, ChangedAt);

        var error = act.Should().Throw<DomainException>().Which;
        error.Code.Should().Be("CATEGORY_HAS_CHILDREN");
        error.Message.Should().Be(
            "امکان حذف دسته‌بندی دارای کالا یا زیردسته وجود ندارد. ابتدا وابستگی‌ها را منتقل کنید");
    }

    [Fact]
    public void SetStatus_Deleted_Must_Record_Deletion_For_A_Leaf()
    {
        var category = CreateCategory();
        category.SetStatus(CategoryStatus.Active, parentIsActive: false,
            hasDependencies: false, CreatedAt.AddMinutes(30));
        category.ClearDomainEvents();

        category.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, ChangedAt);

        category.Status.Should().Be(CategoryStatus.Deleted);
        category.DeletedAt.Should().Be(ChangedAt);
        category.UpdatedAt.Should().Be(ChangedAt);
        category.DomainEvents.Should().ContainSingle().Which
            .Should().BeOfType<CategoryDeletedDomainEvent>();
    }
    [Fact]
    public void SetStatus_Must_Be_Idempotent_For_A_NonDeleted_Current_Status()
    {
        var category = CreateCategory();
        category.ClearDomainEvents();

        category.SetStatus(CategoryStatus.Inactive, parentIsActive: false,
            hasDependencies: false, ChangedAt);

        category.Status.Should().Be(CategoryStatus.Inactive);
        category.UpdatedAt.Should().BeNull();
        category.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SetStatus_Must_Reject_An_Undefined_Status()
    {
        var category = CreateCategory();

        var act = () => category.SetStatus(
            (CategoryStatus)999, parentIsActive: false, hasDependencies: false, ChangedAt);

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_STATUS_INVALID");
        category.Status.Should().Be(CategoryStatus.Inactive);
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("Move")]
    [InlineData("SetActive")]
    [InlineData("SetInactive")]
    [InlineData("SetDeleted")]
    public void Deleted_Category_Must_Reject_Further_Mutations(string mutation)
    {
        var category = CreateCategory();
        SetPersistedId(category, 73);
        category.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, ChangedAt);

        Action act = mutation switch
        {
            "Update" => () => category.Update("New", "new", null, 0, ChangedAt.AddMinutes(1)),
            "Move" => () => category.Move(25, 0, ChangedAt.AddMinutes(1)),
            "SetActive" => () => category.SetStatus(CategoryStatus.Active, true, false,
                ChangedAt.AddMinutes(1)),
            "SetInactive" => () => category.SetStatus(CategoryStatus.Inactive, false, false,
                ChangedAt.AddMinutes(1)),
            "SetDeleted" => () => category.SetStatus(CategoryStatus.Deleted, false, false,
                ChangedAt.AddMinutes(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_DELETED");
    }
    [Fact]
    public void Assignment_Create_Must_Preserve_Composite_Identity_And_Audit_Data()
    {
        var productId= 1; /*= Guid.Parse("10000000-0000-0000-0000-000000000001");*/
        var actorId = Guid.Parse("20000000-0000-0000-0000-000000000002");

        var assignment = CategoryProduct.Create(73, productId, ChangedAt, actorId);

        assignment.CategoryId.Should().Be(73);
        assignment.ProductId.Should().Be(productId);
        assignment.AssignedAt.Should().Be(ChangedAt);
        assignment.AssignedByUserId.Should().Be(actorId);
    }

    [Fact]
    public void Create_Must_Keep_Distinct_Transient_Categories_Unequal()
    {
        var first = CreateCategory();
        var second = CreateCategory();

        first.Should().NotBe(second);
        (first == second).Should().BeFalse();
        first.GetHashCode().Should().NotBe(0);
        new HashSet<Category> { first, second }.Should().HaveCount(2);
    }

    [Fact]
    public void Persisted_Categories_With_The_Same_Id_Must_Have_The_Same_Identity()
    {
        var first = CreateCategory();
        var second = CreateCategory();
        SetPersistedId(first, 73);
        SetPersistedId(second, 73);

        first.Should().Be(second);
        (first == second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Persistence_Assigning_An_Id_Must_Not_Break_Hash_Based_Lookup()
    {
        var category = CreateCategory();
        var transientHashCode = category.GetHashCode();
        var categories = new HashSet<Category> { category };

        SetPersistedId(category, 73);

        category.GetHashCode().Should().Be(transientHashCode);
        categories.Should().Contain(category);
    }

    [Fact]
    public void Assignments_With_The_Same_Category_And_Product_Must_Have_The_Same_Identity()
    {
        var productId =1;
        var first = CategoryProduct.Create(73, productId, CreatedAt);
        var second = CategoryProduct.Create(73, productId, ChangedAt, Guid.NewGuid());

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        new HashSet<CategoryProduct> { first, second }.Should().ContainSingle();
    }

    [Fact]
    public void Assignments_With_A_Different_Composite_Key_Must_Have_Different_Identity()
    {
        var firstProductId = 1;
        var secondProductId = 2;

        var differentCategory = CategoryProduct.Create(74, firstProductId, CreatedAt);
        var differentProduct = CategoryProduct.Create(73, secondProductId, CreatedAt);
        var original = CategoryProduct.Create(73, firstProductId, CreatedAt);

        original.Should().NotBe(differentCategory);
        original.Should().NotBe(differentProduct);
        (original != differentCategory).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Assignment_Create_Must_Reject_A_Non_Positive_Category_Id(long categoryId)
    {
        var act = () => CategoryProduct.Create(categoryId, 1, CreatedAt);

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_PRODUCT_INVALID");
    }

    [Fact]
    public void Assignment_Create_Must_Reject_An_Empty_Product_Id()
    {
        var act = () => CategoryProduct.Create(73, 0, CreatedAt);

        act.Should().Throw<DomainException>().Which.Code
            .Should().Be("CATEGORY_PRODUCT_INVALID");
    }
    private static Category CreateCategory(
        string name = "Category",
        string slug = "category",
        string? description = null,
        long? parentId = null,
        int displayOrder = 0) =>
        Category.Create(Interlocked.Increment(ref _nextCategoryId),
            name, slug, description, parentId, displayOrder, CreatedAt);

    private static void SetPersistedId(Category category, long id) =>
        typeof(Category).GetProperty(nameof(Category.Id))!.SetValue(category, id);
}
