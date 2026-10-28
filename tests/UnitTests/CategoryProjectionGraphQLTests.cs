using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL;
using EwaysVNext.Modules.Catalog.QueryApplication.Categories;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Xunit;

namespace EwaysVNext.UnitTests;

public sealed class CategoryProjectionGraphQLTests
{
    [Fact]
    public async Task Categories_select_only_fields_requested_by_the_GraphQl_selection_set()
    {
        var commandCapture = new CommandCaptureInterceptor();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddDbContext<EwaysVNextDbContext>(options => options
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=CategoryProjectionProbe;Trusted_Connection=True")
            .AddInterceptors(new SuppressConnectionOpeningInterceptor(), commandCapture));
        services.AddScoped<ICategoryAdminQueries, CategoryAdminQueries>();
        services.AddCatalogGraphQL();

        await using var provider = services.BuildServiceProvider();
        var executor = await provider.GetRequestExecutorAsync();

        var result = await executor.ExecuteAsync("{ categories(first: 1) { nodes { id } } }");

        commandCapture.CommandText.Should().NotBeNullOrWhiteSpace(result.ToJson());
        commandCapture.CommandText.Should().NotContain("[Name]");
        commandCapture.CommandText.Should().NotContain("[Slug]");
        commandCapture.CommandText.Should().NotContain("[Description]");
        commandCapture.CommandText.Should().NotContain("[Status]");
        commandCapture.CommandText.Should().NotContain("LEFT JOIN");
        commandCapture.CommandText.Should().NotContain("COUNT(*)");
    }

    private sealed class SuppressConnectionOpeningInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        public string? CommandText { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandText = command.CommandText;
            return ValueTask.FromException<InterceptionResult<DbDataReader>>(
                new QueryReachedDatabaseException());
        }
    }

    private sealed class QueryReachedDatabaseException : Exception;
}
