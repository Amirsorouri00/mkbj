using AwesomeAssertions;
using EwaysVNext.Modules.Catalog.Contracts.Categories;
using EwaysVNext.Modules.Catalog.Application.Categories.Domain;
using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;
using Xunit;
using EwaysVNext.Database;

namespace EwaysVNext.UnitTests;

public sealed class CategoryQueryTests
{
    [Fact]
    public async Task Admin_Search_With_Name_And_Status_Must_Be_SQL_Server_Translatable()
    {
        await using var db = new EwaysVNextDbContext(CreateTranslationProbeOptions());
        var queries = new CategoryAdminQueries(db);

        var query = queries.Search(
            new CategoryAdminFilterDto("Test", CategoryStatus.Active));
        var act = () => query.ToListAsync();

        await act.Should().ThrowExactlyAsync<QueryReachedDatabaseException>(
            "a translatable query reaches connection opening before the probe stops execution");
    }

    [Fact]
    public async Task Admin_Get_By_Id_Must_Be_SQL_Server_Translatable()
    {
        await using var db = new EwaysVNextDbContext(CreateTranslationProbeOptions());
        var queries = new CategoryAdminQueries(db);

        var act = () => queries.GetByIdAsync(123);

        await act.Should().ThrowExactlyAsync<QueryReachedDatabaseException>(
            "a translatable lookup reaches command execution before the probe stops it");
    }

    [Fact]
    public async Task Admin_Get_Children_Must_Be_SQL_Server_Translatable()
    {
        await using var db = new EwaysVNextDbContext(CreateTranslationProbeOptions());
        var queries = new CategoryAdminQueries(db);

        var act = () => queries.GetChildrenAsync(123);

        await act.Should().ThrowExactlyAsync<QueryReachedDatabaseException>(
            "a translatable children query reaches command execution before the probe stops it");
    }

    [Fact]
    public async Task Admin_Search_includes_inactive_and_deleted_categories_and_supports_status_name_and_stable_ordering()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var active = fixture.AddCategory("Active Offer", "active-offer", active: true);
        fixture.AddCategory("Inactive Offer", "inactive-offer");
        var deleted = fixture.AddCategory("Deleted Offer", "deleted-offer");
        deleted.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, DateTimeOffset.UtcNow);
        await fixture.SaveAsync();

        var all = await fixture.AdminQueries.Search(null).ToListAsync();
        all.Should().HaveCount(3);
        all.Select(item => item.Id).Should().Equal(active.Id, active.Id + 1, deleted.Id);

        var inactive = await fixture.AdminQueries.Search(
            new CategoryAdminFilterDto("inactive", CategoryStatus.Inactive)).ToListAsync();
        inactive.Should().ContainSingle(item =>
            item.Name == "Inactive Offer" && item.Status == CategoryStatus.Inactive);

        var deletedOnly = await fixture.AdminQueries.Search(
            new CategoryAdminFilterDto("deleted", CategoryStatus.Deleted)).ToListAsync();
        deletedOnly.Should().ContainSingle(item =>
            item.Id == deleted.Id && item.Status == CategoryStatus.Deleted);
    }

    [Fact]
    public async Task Admin_search_projects_the_immediate_parent_summary_and_null_for_a_root()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root");
        var child = fixture.AddCategory("Child", "child", root.Id);
        await fixture.SaveAsync();

        var categories = await fixture.AdminQueries.Search(null).ToListAsync();

        categories.Single(category => category.Id == root.Id).Parent.Should().BeNull();
        categories.Single(category => category.Id == child.Id).Parent.Should().Be(
            new CategoryAdminParentDto(root.Id, "Root"));
    }

    [Fact]
    public async Task Admin_search_counts_only_non_deleted_direct_children()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root");
        var child = fixture.AddCategory("Child", "child", root.Id);
        fixture.AddCategory("Grandchild", "grandchild", child.Id);
        var deletedChild = fixture.AddCategory("Deleted child", "deleted-child", root.Id);
        deletedChild.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, DateTimeOffset.UtcNow);
        await fixture.SaveAsync();

        var categories = await fixture.AdminQueries.Search(null).ToListAsync();

        categories.Single(category => category.Id == root.Id).ChildCount.Should().Be(1);
        categories.Single(category => category.Id == child.Id).ChildCount.Should().Be(1);
        categories.Single(category => category.Id == deletedChild.Id).ChildCount.Should().Be(0);
    }

    [Fact]
    public async Task Admin_detail_children_and_assignments_include_non_public_rows()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var parent = fixture.AddCategory("Inactive Parent", "inactive-parent");
        var child = fixture.AddCategory("Deleted Child", "deleted-child", parent.Id);
        child.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, DateTimeOffset.UtcNow);
        var productId = 1;
        fixture.AddAssignment(child.Id, productId);
        await fixture.SaveAsync();

        var detail = await fixture.AdminQueries.GetByIdAsync(child.Id);
        detail.IsSuccess.Should().BeTrue();
        detail.Value!.Status.Should().Be(CategoryStatus.Deleted);

        var children = await fixture.AdminQueries.GetChildrenAsync(parent.Id);
        children.Should().ContainSingle(item =>
            item.Id == child.Id && item.Status == CategoryStatus.Deleted);

        var assignments = await fixture.AdminQueries.GetProductIdsAsync(child.Id);
        assignments.IsSuccess.Should().BeTrue();
        (await assignments.Value!.ToListAsync()).Should().ContainSingle().Which.Should().Be(productId);
    }

    [Fact]
    public async Task Admin_find_node_returns_the_existing_node_and_not_found_for_a_missing_node()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var category = fixture.AddCategory("Node", "node");
        await fixture.SaveAsync();

        var existing = await fixture.AdminQueries.GetByIdAsync(category.Id);
        var missing = await fixture.AdminQueries.GetByIdAsync(999_999);

        existing.Value!.Id.Should().Be(category.Id);
        missing.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
    }

    [Fact]
    public async Task Admin_find_parent_returns_null_for_a_root()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root");
        await fixture.SaveAsync();

        var parent = await fixture.AdminQueries.GetParentAsync(root.Id);

        parent.IsSuccess.Should().BeTrue();
        parent.Value.Should().BeNull();
    }

    [Fact]
    public async Task Admin_find_parent_returns_only_the_immediate_parent_of_a_nested_category()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root");
        var child = fixture.AddCategory("Child", "child", root.Id);
        var nested = fixture.AddCategory("Nested", "nested", child.Id);
        await fixture.SaveAsync();

        var childParent = await fixture.AdminQueries.GetParentAsync(child.Id);
        var nestedParent = await fixture.AdminQueries.GetParentAsync(nested.Id);

        childParent.Value!.Id.Should().Be(root.Id);
        nestedParent.Value!.Id.Should().Be(child.Id);
    }

    [Fact]
    public async Task Admin_find_parent_preserves_not_found_for_a_missing_source_category()
    {
        await using var fixture = await QueryFixture.CreateAsync();

        var parent = await fixture.AdminQueries.GetParentAsync(999_999);

        parent.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
    }

    [Fact]
    public async Task Admin_find_descendants_returns_an_ordered_nested_tree_without_the_source()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root");
        var later = fixture.AddCategory("Later", "later", root.Id);
        var earlier = fixture.AddCategory("Earlier", "earlier", root.Id);
        var nested = fixture.AddCategory("Nested", "nested", earlier.Id);
        QueryFixture.SetDisplayOrder(later, 2);
        QueryFixture.SetDisplayOrder(earlier, 1);
        await fixture.SaveAsync();

        var descendants = await fixture.AdminQueries.GetDescendantsAsync(root.Id);

        descendants.Value!.Select(node => node.Id).Should().Equal(earlier.Id, later.Id);
        descendants.Value![0].Children.Should().ContainSingle().Which.Id.Should().Be(nested.Id);
        Flatten(descendants.Value).Should().NotContain(node => node.Id == root.Id);
    }

    [Fact]
    public async Task Admin_find_descendants_returns_empty_for_a_leaf_and_not_found_for_a_missing_source()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var leaf = fixture.AddCategory("Leaf", "leaf");
        await fixture.SaveAsync();

        var empty = await fixture.AdminQueries.GetDescendantsAsync(leaf.Id);
        var missing = await fixture.AdminQueries.GetDescendantsAsync(999_999);

        empty.IsSuccess.Should().BeTrue();
        empty.Value.Should().BeEmpty();
        missing.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
    }

    [Fact]
    public async Task Admin_find_descendants_includes_inactive_and_soft_deleted_nodes_cycle_safely()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root", active: true);
        var inactive = fixture.AddCategory("Inactive", "inactive", root.Id);
        var deleted = fixture.AddCategory("Deleted", "deleted", inactive.Id);
        deleted.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, DateTimeOffset.UtcNow);
        await fixture.SaveAsync();

        var descendants = await fixture.AdminQueries.GetDescendantsAsync(root.Id);

        var inactiveNode = descendants.Value.Should().ContainSingle().Which;
        inactiveNode.Id.Should().Be(inactive.Id);
        inactiveNode.Status.Should().Be(CategoryStatus.Inactive);
        inactiveNode.Children.Should().ContainSingle(node =>
            node.Id == deleted.Id && node.Status == CategoryStatus.Deleted);
    }

    [Fact]
    public async Task Admin_find_descendants_executes_one_EF_query_for_a_multilevel_tree()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root");
        var child = fixture.AddCategory("Child", "child", root.Id);
        fixture.AddCategory("Nested", "nested", child.Id);
        await fixture.SaveAsync();
        fixture.QueryCounter.Reset();

        var descendants = await fixture.AdminQueries.GetDescendantsAsync(root.Id);

        descendants.IsSuccess.Should().BeTrue();
        fixture.QueryCounter.Count.Should().Be(1,
            "descendants must be materialized by one EF query rather than one query per level");
    }

    [Fact]
    public async Task Admin_hierarchy_includes_inactive_and_deleted_rows_without_tracking_them()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root", active: true);
        var child = fixture.AddCategory("Inactive child", "inactive-child", root.Id);
        var deleted = fixture.AddCategory("Deleted grandchild", "deleted-grandchild", child.Id);
        deleted.SetStatus(CategoryStatus.Deleted, parentIsActive: false,
            hasDependencies: false, DateTimeOffset.UtcNow);
        await fixture.SaveAsync();
        fixture.Db.ChangeTracker.Clear();

        var hierarchy = await fixture.AdminQueries.GetHierarchyAsync();

        var rootNode = hierarchy.Should().ContainSingle().Which;
        rootNode.Id.Should().Be(root.Id);
        rootNode.Status.Should().Be(CategoryStatus.Active);
        var childNode = rootNode.Children.Should().ContainSingle().Which;
        childNode.Id.Should().Be(child.Id);
        childNode.Status.Should().Be(CategoryStatus.Inactive);
        childNode.Children.Should().ContainSingle(node =>
            node.Id == deleted.Id && node.Status == CategoryStatus.Deleted);
        fixture.Db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_hierarchy_terminates_for_stored_cycles_and_honors_cancellation()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var first = fixture.AddCategory("First", "first");
        var second = fixture.AddCategory("Second", "second", first.Id);
        QueryFixture.SetParent(first, second.Id);
        await fixture.SaveAsync();

        var hierarchy = await fixture.AdminQueries.GetHierarchyAsync();
        var ids = Flatten(hierarchy).Select(node => node.Id).ToArray();
        ids.Should().BeEquivalentTo([first.Id, second.Id]);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var act = () => fixture.AdminQueries.GetHierarchyAsync(cancellation.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        static IEnumerable<CategoryAdminTreeNodeDto> Flatten(
            IEnumerable<CategoryAdminTreeNodeDto> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children)) yield return child;
            }
        }
    }

    [Fact]
    public async Task Admin_search_remains_deferred_until_the_IQueryable_is_enumerated()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        fixture.AddCategory("Root", "root", active: true);
        await fixture.SaveAsync();
        fixture.QueryCounter.Reset();

        var query = fixture.AdminQueries.Search(null);

        fixture.QueryCounter.Count.Should().Be(0);
        (await query.ToListAsync()).Should().ContainSingle();
        fixture.QueryCounter.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Admin_product_query_only_executes_the_existence_check_before_paging()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var category = fixture.AddCategory("Root", "root", active: true);
        fixture.AddAssignment(category.Id, 10);
        fixture.AddAssignment(category.Id, 20);
        await fixture.SaveAsync();
        fixture.QueryCounter.Reset();

        var result = await fixture.AdminQueries.GetProductIdsAsync(category.Id);

        result.IsSuccess.Should().BeTrue();
        fixture.QueryCounter.Count.Should().Be(1,
            "the category existence check is the only eager database query");
        fixture.QueryCounter.Reset();
        (await result.Value!.Take(1).ToListAsync()).Should().Equal(10);
        fixture.QueryCounter.Count.Should().BeGreaterThan(0,
            "the product query must stay deferred for Hot Chocolate paging");
    }

    [Fact]
    public async Task Tree_Excludes_A_Category_When_Any_Ancestor_Is_Inactive()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root", active: true);
        var child = fixture.AddCategory("Child", "child", root.Id, active: true);
        fixture.AddCategory("Leaf", "leaf", child.Id, active: true);
        root.SetStatus(CategoryStatus.Inactive, parentIsActive: false,
            hasDependencies: false, DateTimeOffset.UtcNow);
        await fixture.SaveAsync();

        var tree = await fixture.Queries.GetTreeAsync();

        tree.Should().BeEmpty();
    }

    [Fact]
    public async Task Tree_Returns_Active_NonDeleted_Hierarchy_With_Nested_Children()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root", active: true);
        var child = fixture.AddCategory("Child", "child", root.Id, active: true);
        fixture.AddCategory("Leaf", "leaf", child.Id, active: true);
        await fixture.SaveAsync();

        var tree = await fixture.Queries.GetTreeAsync();

        tree.Should().ContainSingle().Which.Children.Should().ContainSingle()
            .Which.Children.Should().ContainSingle();
    }

    [Fact]
    public async Task Category_Lookup_Requires_The_Full_Ancestor_Chain_To_Be_Visible()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var root = fixture.AddCategory("Root", "root", active: true);
        var child = fixture.AddCategory("Child", "child", root.Id, active: true);
        await fixture.SaveAsync();
        root.SetStatus(CategoryStatus.Inactive, parentIsActive: false,
            hasDependencies: false, DateTimeOffset.UtcNow);
        await fixture.SaveAsync();

        var result = await fixture.Queries.GetByIdAsync(child.Id);

        result.IsFailure.Should().BeTrue();
        result.Error!.Code.Should().Be("CATEGORY_NOT_FOUND");
    }

    [Fact]
    public async Task Category_Products_Are_Paged_By_Product_Id()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var category = fixture.AddCategory("Root", "root", active: true);
        var ids = Enumerable.Range(1, 3).Select(id => (long)id).OrderBy(id => id).ToArray();
        foreach (var id in ids) fixture.AddAssignment(category.Id, id);
        await fixture.SaveAsync();

        var page = await fixture.Queries.GetProductIdsAsync(category.Id, 2, 1);

        page.IsSuccess.Should().BeTrue();
        page.Value!.ProductIds.Should().Equal(ids[1]);
        page.Value.Page.Should().Be(2);
        page.Value.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task Reverse_Product_Lookup_Returns_Only_Effectively_Visible_Categories()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var visible = fixture.AddCategory("Visible", "visible", active: true);
        var hidden = fixture.AddCategory("Hidden", "hidden", active: false);
        var productId = 1;
        fixture.AddAssignment(visible.Id, productId);
        fixture.AddAssignment(hidden.Id, productId);
        await fixture.SaveAsync();

        var categories = await fixture.Queries.GetByProductIdAsync(productId);

        categories.Select(category => category.Id).Should().Equal(visible.Id);
    }

    [Fact]
    public async Task Reverse_Product_Lookup_Rejects_Cycle_And_Orphan_Ancestors()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var cycle = fixture.AddCategory("Cycle", "reverse-cycle", active: true);
        var orphan = fixture.AddCategory("Orphan", "reverse-orphan", active: true);
        QueryFixture.SetParent(cycle, cycle.Id);
        QueryFixture.SetParent(orphan, 999_999);
        var productId = 1;
        fixture.AddAssignment(cycle.Id, productId);
        fixture.AddAssignment(orphan.Id, productId);
        await fixture.SaveAsync();

        var categories = await fixture.Queries.GetByProductIdAsync(productId);

        categories.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Product_Page_Rejects_Invalid_Page_Arguments(int page, int pageSize)
    {
        await using var fixture = await QueryFixture.CreateAsync();

        var result = await fixture.Queries.GetProductIdsAsync(1, page, pageSize);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Public_Reads_Do_Not_Track_Categories_Or_Assignments()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var category = fixture.AddCategory("Root", "root", active: true);
        fixture.AddAssignment(category.Id, 1);
        await fixture.SaveAsync();
        fixture.Db.ChangeTracker.Clear();

        _ = await fixture.Queries.GetTreeAsync();
        _ = await fixture.Queries.GetByIdAsync(category.Id);
        _ = await fixture.Queries.GetProductIdsAsync(category.Id, 1, 10);

        fixture.Db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task Tree_Excludes_Cycles_And_Orphaned_Ancestors()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var cycle = fixture.AddCategory("Cycle", "cycle", active: true);
        QueryFixture.SetParent(cycle, cycle.Id);
        var orphan = fixture.AddCategory("Orphan", "orphan", active: true);
        QueryFixture.SetParent(orphan, 999_999);
        await fixture.SaveAsync();

        var tree = await fixture.Queries.GetTreeAsync();

        tree.Should().BeEmpty();
    }

    [Fact]
    public async Task Tree_Orders_Siblings_By_Display_Order_Then_Id()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var first = fixture.AddCategory("First", "first", active: true);
        var second = fixture.AddCategory("Second", "second", active: true);
        QueryFixture.SetDisplayOrder(first, 2);
        QueryFixture.SetDisplayOrder(second, 1);
        await fixture.SaveAsync();

        var tree = await fixture.Queries.GetTreeAsync();

        tree.Select(node => node.Name).Should().Equal("Second", "First");
    }

    [Fact]
    public async Task Product_Page_With_A_Large_Page_Number_Returns_An_Empty_Page_Without_Overflow()
    {
        await using var fixture = await QueryFixture.CreateAsync();
        var category = fixture.AddCategory("Root", "root", active: true);
        await fixture.SaveAsync();

        var page = await fixture.Queries.GetProductIdsAsync(category.Id, int.MaxValue, 100);

        page.IsSuccess.Should().BeTrue();
        page.Value!.ProductIds.Should().BeEmpty();
    }

    private sealed class QueryFixture : IAsyncDisposable
    {
        private long _nextCategoryId = 100;

        private QueryFixture(EwaysVNextDbContext db, ICategoryQueries queries,
            ICategoryAdminQueries adminQueries, QueryExecutionCounter queryCounter)
        {
            Db = db;
            Queries = queries;
            AdminQueries = adminQueries;
            QueryCounter = queryCounter;
        }

        public EwaysVNextDbContext Db { get; }
        public ICategoryQueries Queries { get; }
        public ICategoryAdminQueries AdminQueries { get; }
        public QueryExecutionCounter QueryCounter { get; }

        public static async Task<QueryFixture> CreateAsync()
        {
            var queryCounter = new QueryExecutionCounter();
            var options = new DbContextOptionsBuilder<EwaysVNextDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .LogTo(queryCounter.Record,
                    [CoreEventId.QueryExecutionPlanned], LogLevel.Debug)
                .Options;
            var db = new EwaysVNextDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new QueryFixture(db, new CategoryQueries(db),
                new CategoryAdminQueries(db), queryCounter);
        }

        public Category AddCategory(string name, string slug, long? parentId = null, bool active = false)
        {
            var category = Category.Create(Interlocked.Increment(ref _nextCategoryId),
                name, slug, null, parentId, 0, DateTimeOffset.UtcNow);
            Db.Categories.Add(category);
            Db.SaveChanges();
            if (active)
            {
                category.SetStatus(CategoryStatus.Active,
                    parentId is null || Db.Categories.Find(parentId)!.Status == CategoryStatus.Active,
                    hasDependencies: false, DateTimeOffset.UtcNow);
                Db.SaveChanges();
            }
            return category;
        }

        public void AddAssignment(long categoryId, long productId) =>
            Db.CategoryProducts.Add(CategoryProduct.Create(categoryId, productId, DateTimeOffset.UtcNow));

        public static void SetParent(Category category, long parentId) =>
            typeof(Category).GetProperty(nameof(Category.ParentId))!.SetValue(category, parentId);

        public static void SetDisplayOrder(Category category, int displayOrder) =>
            typeof(Category).GetProperty(nameof(Category.DisplayOrder))!.SetValue(category, displayOrder);

        public Task<int> SaveAsync() => Db.SaveChangesAsync();
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class QueryExecutionCounter
    {
        public int Count { get; private set; }
        public void Record(string _) => Count++;
        public void Reset() => Count = 0;
    }

    private static DbContextOptions<EwaysVNextDbContext> CreateTranslationProbeOptions() =>
        new DbContextOptionsBuilder<EwaysVNextDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=category_query_translation;Username=probe;Password=probe;Timeout=1").UseSnakeCaseNamingConvention()
            .AddInterceptors(
                new SuppressConnectionOpeningInterceptor(),
                new QueryTranslationProbeInterceptor())
            .Options;

    private sealed class SuppressConnectionOpeningInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class QueryTranslationProbeInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult<DbDataReader>>(
                new QueryReachedDatabaseException());
    }

    private sealed class QueryReachedDatabaseException : Exception;

    private static IEnumerable<CategoryAdminTreeNodeDto> Flatten(
        IEnumerable<CategoryAdminTreeNodeDto> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
}
