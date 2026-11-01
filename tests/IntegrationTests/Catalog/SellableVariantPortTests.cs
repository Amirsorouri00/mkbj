using AwesomeAssertions;
using EwaysVNext.Modules.Catalog.Application.Products.Ports.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EwaysVNext.IntegrationTests.Catalog;

/// <summary>
/// The sellability version is read by carts to tell whether what they hold is still the thing
/// they were priced against, so these run against a real sequence — the grants, the schema and
/// <c>nextval</c>'s transaction-free behaviour are all things only Postgres can confirm.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class SellableVariantPortTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Sellability_Version_Increases_After_Notification()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICatalogChangeNotifier>();

        var before = await notifier.CurrentVersionAsync(TestContext.Current.CancellationToken);
        await notifier.SellabilityChangedAsync(TestContext.Current.CancellationToken);
        var after = await notifier.CurrentVersionAsync(TestContext.Current.CancellationToken);

        after.Should().BeGreaterThan(before,
            "a cart compares this number to decide whether its contents went stale");
    }

    [Fact]
    public async Task A_Display_Change_Leaves_The_Version_Alone()
    {
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICatalogChangeNotifier>();

        var before = await notifier.CurrentVersionAsync(TestContext.Current.CancellationToken);
        await notifier.DisplayChangedAsync(TestContext.Current.CancellationToken);
        var after = await notifier.CurrentVersionAsync(TestContext.Current.CancellationToken);

        after.Should().Be(before,
            "a retitled product or a new image changes nothing about what a cart holds, and "
            + "bumping the version would invalidate every cart in the system over a typo");
    }

    [Fact]
    public async Task The_Application_Role_May_Advance_The_Sequence()
    {
        // Tests connect as eways_app, which owns nothing. If the default privileges in
        // 01-roles.sql ever stop covering sequences, this is where it shows.
        using var factory = fixture.CreateFactory();
        using var scope = factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<ICatalogChangeNotifier>();

        var act = async () => await notifier.CurrentVersionAsync(TestContext.Current.CancellationToken);

        await act.Should().NotThrowAsync();
    }
}
