using System.Data.Common;
using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Inbound;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.Adapter.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Domain;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Ports.Outbound;
using EwaysVNext.Modules.Catalog.Application.AttributeBlueprints.Services;
using EwaysVNext.Modules.Catalog.QueryApplication.Abstractions.AttributeBlueprints;
using EwaysVNext.Modules.Catalog.QueryApplication.AttributeBlueprints;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class AttributeBlueprintFilteringTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resolver_Combines_Filters_Before_Paging_Through_Real_Read_Path()
    {
        await using var services = CreateServices();
        await SeedAsync(services);
        var resolver = new AttributeBlueprintQueries();
        var provider = services.GetRequiredService<IAttributeBlueprintQueryService>();
        var filter = new AttributeBlueprintFilter("  Color  ", AttributeBlueprintStatusFilter.Active, AttributeBlueprintDataTypeFilter.Text);

        var first = await resolver.AttributeBlueprints(1, 1, false, provider, filter, TestContext.Current.CancellationToken);
        var second = await resolver.AttributeBlueprints(2, 1, false, provider, filter, TestContext.Current.CancellationToken);

        first.Items.Select(x => x.Id).Should().Equal(10);
        second.Items.Select(x => x.Id).Should().Equal(11);
        first.Page.Should().Be(1);
        second.Page.Should().Be(2);
        first.PageSize.Should().Be(1);
        second.PageSize.Should().Be(1);
        first.TotalCount.Should().Be(2);
        second.TotalCount.Should().Be(2);
        first.PageCount.Should().Be(2);
        second.PageCount.Should().Be(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_Name_And_Omitted_Filter_Preserve_Normal_List(string? name)
    {
        await using var services = CreateServices();
        await SeedAsync(services);
        var repository = services.GetRequiredService<IAttributeBlueprintRepository>();

        var unfiltered = await repository.ListSnapshotsAsync(1, 100, ct: TestContext.Current.CancellationToken);
        var filtered = await repository.ListSnapshotsAsync(1, 100, filter: new(NameContains: name), ct: TestContext.Current.CancellationToken);

        filtered.Should().BeEquivalentTo(unfiltered, options => options.WithStrictOrdering());
        filtered.Items.Should().HaveCount(7);
        filtered.Items.Should().OnlyContain(x => x.DeletedAt == null);
        filtered.TotalCount.Should().Be(7);
    }

    [Theory]
    [InlineData(AttributeBlueprintStatusFilter.Active, new long[] { 1, 10, 11, 20, 40 })]
    [InlineData(AttributeBlueprintStatusFilter.Inactive, new long[] { 30 })]
    [InlineData(AttributeBlueprintStatusFilter.Archived, new long[] { 50 })]
    public async Task Status_Filter_Maps_All_Contract_Values(AttributeBlueprintStatusFilter status, long[] expected)
    {
        await using var services = CreateServices();
        await SeedAsync(services);

        var result = await services.GetRequiredService<IAttributeBlueprintRepository>()
            .ListSnapshotsAsync(1, 100, filter: new(Status: status), ct: TestContext.Current.CancellationToken);

        result.Items.Select(x => x.Id).Should().BeEquivalentTo(expected);
        result.TotalCount.Should().Be(expected.Length);
    }

    [Theory]
    [InlineData(AttributeBlueprintDataTypeFilter.Text, new long[] { 1, 10, 11, 30 })]
    [InlineData(AttributeBlueprintDataTypeFilter.Number, new long[] { 20 })]
    [InlineData(AttributeBlueprintDataTypeFilter.Boolean, new long[] { 40 })]
    [InlineData(AttributeBlueprintDataTypeFilter.Date, new long[] { 50 })]
    public async Task DataType_Filter_Maps_All_Contract_Values(AttributeBlueprintDataTypeFilter dataType, long[] expected)
    {
        await using var services = CreateServices();
        await SeedAsync(services);

        var result = await services.GetRequiredService<IAttributeBlueprintRepository>()
            .ListSnapshotsAsync(1, 100, filter: new(DataType: dataType), ct: TestContext.Current.CancellationToken);

        result.Items.Select(x => x.Id).Should().BeEquivalentTo(expected);
        result.TotalCount.Should().Be(expected.Length);
    }

    [Fact]
    public async Task IncludeDeleted_Still_Applies_Filters_And_Orders_Duplicate_Names_By_Id()
    {
        await using var services = CreateServices();
        await SeedAsync(services);
        var repository = services.GetRequiredService<IAttributeBlueprintRepository>();
        var filter = new AttributeBlueprintFilter("Color", AttributeBlueprintStatusFilter.Inactive);

        var visible = await repository.ListSnapshotsAsync(1, 100, filter: filter, ct: TestContext.Current.CancellationToken);
        var includingDeleted = await repository.ListSnapshotsAsync(1, 100, true, filter, TestContext.Current.CancellationToken);

        visible.Items.Select(x => x.Id).Should().Equal(30);
        includingDeleted.Items.Select(x => x.Id).Should().Equal(30, 60);
        visible.TotalCount.Should().Be(1);
        includingDeleted.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Unmatched_Name_Returns_Empty_List()
    {
        await using var services = CreateServices();
        await SeedAsync(services);

        var result = await services.GetRequiredService<IAttributeBlueprintRepository>()
            .ListSnapshotsAsync(1, 10, filter: new(NameContains: "missing"), ct: TestContext.Current.CancellationToken);

        result.Items.Should().BeEmpty();
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(10);
        result.TotalCount.Should().Be(0);
        result.PageCount.Should().Be(0);
    }

    [Fact]
    public async Task Detail_And_List_Include_Predefined_Options()
    {
        await using var services = CreateServices();
        var db = services.GetRequiredService<EwaysVNextDbContext>();
        var blueprint = AttributeBlueprint.Create(42, "Color", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Text, Now);
        blueprint.AddOption(4202, "Blue", null, Now);
        blueprint.AddOption(4201, "Red", null, Now);
        db.AttributeBlueprints.Add(blueprint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var repository = services.GetRequiredService<IAttributeBlueprintRepository>();

        var detail = await repository.GetSnapshotByIdAsync(42, TestContext.Current.CancellationToken);
        var list = await repository.ListSnapshotsAsync(1, 10, ct: TestContext.Current.CancellationToken);

        detail.Should().NotBeNull();
        detail!.Options.Should().Equal(
            new AttributeBlueprintOptionSnapshot(4201, "Red", null),
            new AttributeBlueprintOptionSnapshot(4202, "Blue", null));
        list.Items.Single().Options.Should().Equal(detail.Options);
    }

    [Fact]
    public async Task Write_Repository_Loads_Options_And_Unit_Query_Returns_Central_Units()
    {
        await using var services = CreateServices();
        var db = services.GetRequiredService<EwaysVNextDbContext>();
        db.MeasurementUnits.Add(MeasurementUnit.Create(7, "Kilogram", "kg"));
        var blueprint = AttributeBlueprint.Create(42, "Weight", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Number, Now);
        blueprint.AddOption(4201, "5", 7, Now);
        db.AttributeBlueprints.Add(blueprint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var repository = services.GetRequiredService<IAttributeBlueprintRepository>();
        var measurementUnitRepository = services.GetRequiredService<IMeasurementUnitRepository>();
        var loaded = await repository.GetByIdAsync(42, TestContext.Current.CancellationToken);
        var units = await services.GetRequiredService<IMeasurementUnitQueryService>()
            .ListAsync(TestContext.Current.CancellationToken);

        loaded.Should().NotBeNull();
        loaded!.Options.Should().ContainSingle().Which.MeasurementUnitId.Should().Be(7);
        (await measurementUnitRepository.ExistsAsync(7, TestContext.Current.CancellationToken)).Should().BeTrue();
        units.Should().Equal(new MeasurementUnitSnapshot(7, "Kilogram", "kg"));

        loaded.UpdateOption(4201, "10", 7, Now.AddMinutes(1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var updated = await repository.GetSnapshotByIdAsync(42, TestContext.Current.CancellationToken);
        updated!.Options.Should().ContainSingle().Which.Value.Should().Be("10");

        var reloaded = await repository.GetByIdAsync(42, TestContext.Current.CancellationToken);
        reloaded!.RemoveOption(4201, Now.AddMinutes(2));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        var withoutOption = await repository.GetSnapshotByIdAsync(42, TestContext.Current.CancellationToken);
        withoutOption!.Options.Should().BeEmpty();
    }

    [Fact]
    public async Task Full_Update_Keeps_Option_Id_When_Data_Type_Changes()
    {
        await using var services = CreateServices();
        var db = services.GetRequiredService<EwaysVNextDbContext>();
        db.MeasurementUnits.Add(MeasurementUnit.Create(7, "Kilogram", "kg"));
        var blueprint = AttributeBlueprint.Create(42, "Weight", AttributeBlueprintDataType.Text, Now);
        blueprint.AddOption(4201, "Five", null, Now);
        db.AttributeBlueprints.Add(blueprint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var loaded = await services.GetRequiredService<IAttributeBlueprintRepository>()
            .GetByIdAsync(42, TestContext.Current.CancellationToken);
        loaded.Should().NotBeNull();
        loaded!.UpdateDetails("Weight", AttributeBlueprintDataType.Number, true,
            [(4201, "5.00", 7)], Now.AddMinutes(1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var snapshot = await services.GetRequiredService<IAttributeBlueprintRepository>()
            .GetSnapshotByIdAsync(42, TestContext.Current.CancellationToken);
        snapshot.Should().NotBeNull();
        snapshot!.DataType.Should().Be("Number");
        snapshot.Options.Should().ContainSingle().Which.Should().Be(
            new AttributeBlueprintOptionSnapshot(4201, "5", 7));
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    public async Task Last_And_Out_Of_Range_Pages_Keep_Total_Metadata(int page, int expectedItems)
    {
        await using var services = CreateServices();
        await SeedAsync(services);

        var result = await services.GetRequiredService<IAttributeBlueprintRepository>()
            .ListSnapshotsAsync(page, 3, ct: TestContext.Current.CancellationToken);

        result.Items.Should().HaveCount(expectedItems);
        result.Page.Should().Be(page);
        result.PageSize.Should().Be(3);
        result.TotalCount.Should().Be(7);
        result.PageCount.Should().Be(3);
    }

    [Theory]
    [InlineData(0, 0, 1, 7)]
    [InlineData(-1, 101, 100, 1)]
    public async Task Metadata_Uses_Normalized_Pagination(int page, int pageSize, int expectedPageSize, int expectedPageCount)
    {
        await using var services = CreateServices();
        await SeedAsync(services);
        var provider = services.GetRequiredService<IAttributeBlueprintQueryService>();

        var result = await new AttributeBlueprintQueries()
            .AttributeBlueprints(page, pageSize, false, provider, ct: TestContext.Current.CancellationToken);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(expectedPageSize);
        result.PageCount.Should().Be(expectedPageCount);
        result.TotalCount.Should().Be(7);
        result.Items.Should().HaveCount(Math.Min(expectedPageSize, 7));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Undefined_Contract_Enum_Is_Rejected(bool invalidStatus)
    {
        await using var services = CreateServices();
        var filter = invalidStatus
            ? new AttributeBlueprintFilter(Status: (AttributeBlueprintStatusFilter)999)
            : new AttributeBlueprintFilter(DataType: (AttributeBlueprintDataTypeFilter)999);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => services.GetRequiredService<IAttributeBlueprintRepository>()
            .ListSnapshotsAsync(1, 10, filter: filter, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filtered_Count_Translates_With_SqlServer_Without_Opening_A_Connection(bool includeDeleted)
    {
        await using var services = CreateServices(sqlServer: true);
        var repository = services.GetRequiredService<IAttributeBlueprintRepository>();

        // EF compiles the filtered count before attempting a connection. The interceptor
        // stops there: this proves count translation, not database execution or collation.
        await Assert.ThrowsAsync<TranslationVerifiedException>(() => repository.ListSnapshotsAsync(
            2, 10, includeDeleted,
            new("Color%_", AttributeBlueprintStatusFilter.Active, AttributeBlueprintDataTypeFilter.Text),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Option_Projection_Translates_With_SqlServer_Without_Opening_A_Connection()
    {
        await using var services = CreateServices(sqlServer: true);
        var repository = services.GetRequiredService<IAttributeBlueprintRepository>();

        await Assert.ThrowsAsync<TranslationVerifiedException>(() =>
            repository.GetSnapshotByIdAsync(42, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GraphQL_Schema_Exposes_One_Archive_Status_Mutation()
    {
        await using var services = CreateServices();
        var executor = await services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var schema = executor.Schema.ToString();

        schema.Should().Contain("setAttributeBlueprintArchiveStatus(");
        schema.Should().Contain("isArchived: Boolean!");
        schema.Should().NotContain("archiveAttributeBlueprint(");
        schema.Should().NotContain("unarchiveAttributeBlueprint(");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archived_Blueprint_Reloads_And_Restores_Previous_Status(bool inactive)
    {
        await using var services = CreateServices();
        var db = services.GetRequiredService<EwaysVNextDbContext>();
        var blueprint = AttributeBlueprint.Create(42, "Color", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Text, Now);
        if (inactive) blueprint.Deactivate(Now);
        var expectedStatus = blueprint.Status;
        blueprint.Archive(Now);
        db.AttributeBlueprints.Add(blueprint);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var reloaded = await db.AttributeBlueprints.SingleAsync(TestContext.Current.CancellationToken);
        reloaded.Status.Should().Be(AttributeBlueprintStatus.Archived);
        reloaded.PreviousStatus.Should().Be(expectedStatus);
        reloaded.Unarchive(Now.AddMinutes(1));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();

        var restored = await db.AttributeBlueprints.SingleAsync(TestContext.Current.CancellationToken);
        restored.Status.Should().Be(expectedStatus);
        restored.PreviousStatus.Should().BeNull();
        restored.Name.Should().Be("Color");
        restored.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task GraphQL_Schema_Exposes_Optional_Typed_Filter()
    {
        await using var services = CreateServices();
        var executor = await services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var schema = executor.Schema.ToString();

        schema.Should().Contain("filter: AttributeBlueprintFilterInput");
        schema.Should().NotContain("filter: AttributeBlueprintFilterInput!");
        schema.Should().Contain("nameContains: String");
        schema.Should().Contain("status: AttributeBlueprintStatusFilter");
        schema.Should().Contain("dataType: AttributeBlueprintDataTypeFilter");
    }

    [Fact]
    public async Task GraphQL_List_Returns_Items_And_Pagination_Metadata()
    {
        await using var services = CreateServices();
        var executor = await services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var queryType = executor.Schema.QueryType.Fields["attributeBlueprints"].Type.ToString();
        var schema = executor.Schema.ToString();

        queryType.Should().NotStartWith("[");
        schema.Should().Contain("items: [AttributeBlueprintSnapshot!]!");
        schema.Should().Contain("page: Int!");
        schema.Should().Contain("pageSize: Int!");
        schema.Should().Contain("pageCount: Int!");
        schema.Should().Contain("totalCount: Int!");
    }

    [Fact]
    public async Task GraphQL_Schema_Exposes_Options_Units_And_Option_Mutations()
    {
        await using var services = CreateServices();
        var executor = await services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);
        var schema = executor.Schema.ToString();

        schema.Should().Contain("measurementUnits: [MeasurementUnitSnapshot!]!");
        schema.Should().Contain("options: [AttributeBlueprintOptionSnapshot!]!");
        schema.Should().Contain("createAttributeBlueprint(input: CreateAttributeBlueprintInput!)");
        schema.Should().Contain("options: [AttributeBlueprintOptionInput!]");
        schema.Should().Contain("addAttributeBlueprintOption(input: AddAttributeBlueprintOptionInput!)");
        schema.Should().Contain("updateAttributeBlueprintOption(");
        schema.Should().Contain("input: UpdateAttributeBlueprintOptionInput!");
        schema.Should().Contain("removeAttributeBlueprintOption(");
        schema.Should().Contain("updateAttributeBlueprint(input: UpdateAttributeBlueprintInput!)");
        schema.Should().Contain("options: [AttributeBlueprintOptionUpdateInput!]!");
        schema.Should().NotContain("confirmProductValueRemoval");
    }

    private static ServiceProvider CreateServices(bool sqlServer = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<EwaysVNextDbContext>(options =>
        {
            if (sqlServer)
                options.UseSqlServer("Server=127.0.0.1;Database=TranslationOnly;Integrated Security=true;TrustServerCertificate=true")
                    .AddInterceptors(new StopBeforeConnectionInterceptor());
            else
                options.UseInMemoryDatabase(Guid.NewGuid().ToString());
        });
        services.AddCatalogOutboundAdapters();
        services.AddScoped<IAttributeBlueprintQueryService, AttributeBlueprintQueryService>();
        services.AddScoped<IMeasurementUnitQueryService, MeasurementUnitQueryService>();
        services.AddCatalogGraphQL();
        return services.BuildServiceProvider();
    }

    private static async Task SeedAsync(ServiceProvider services)
    {
        var db = services.GetRequiredService<EwaysVNextDbContext>();
        var inactive = AttributeBlueprint.Create(30, "Color inactive", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Text, Now);
        inactive.Deactivate(Now);
        var archived = AttributeBlueprint.Create(50, "Date", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Date, Now);
        archived.Archive(Now);
        var deleted = AttributeBlueprint.Create(60, "Color inactive", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Text, Now);
        deleted.Delete(Now);
        db.AttributeBlueprints.AddRange(
            deleted, inactive, archived,
            AttributeBlueprint.Create(1, "A noise", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Text, Now),
            AttributeBlueprint.Create(10, "Color A", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Text, Now),
            AttributeBlueprint.Create(11, "Color B", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Text, Now),
            AttributeBlueprint.Create(20, "Color count", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Number, Now),
            AttributeBlueprint.Create(40, "Boolean", Modules.Catalog.Application.AttributeBlueprints.Domain.AttributeBlueprintDataType.Boolean, Now));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class TranslationVerifiedException : Exception;

    private sealed class StopBeforeConnectionInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
            => throw new TranslationVerifiedException();
    }
}
