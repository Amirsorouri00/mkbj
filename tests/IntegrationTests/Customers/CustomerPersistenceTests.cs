using AwesomeAssertions;
using EwaysVNext.Database;
using EwaysVNext.Modules.Customer.Application.Customers.Domain;
using EwaysVNext.Modules.Customer.Application.Customers.Ports.Inbound;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Customers;

/// <summary>
/// What the database guarantees about customers, which is mostly about races: two partners may
/// use the same reference, one partner may not use it twice, and concurrent first requests for
/// one customer must settle on a single row.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class CustomerPersistenceTests(PostgresFixture fixture)
{
    private static string External() => $"ext-{Guid.NewGuid():N}";

    private static ContactInput Contact(string? email = "a@x.com", string? phone = "+6591234567") =>
        new(email, phone, "Martin");

    private static async Task<T> Scoped<T>(
        WebApplicationFactory<Program> factory, Func<IServiceProvider, Task<T>> use)
    {
        using var scope = factory.Services.CreateScope();

        return await use(scope.ServiceProvider);
    }

    [Fact]
    public async Task Same_External_Customer_Id_Under_Two_Partners_Creates_Two_Customers()
    {
        using var factory = fixture.CreateFactory();
        var external = External();

        var first = await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .CreatePartnerCustomerAsync(701, external, Contact(), "corr"));

        var second = await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .CreatePartnerCustomerAsync(702, external, Contact(), "corr"));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        second.Value!.CustomerId.Should().NotBe(first.Value!.CustomerId,
            "the reference is the partner's own, so two partners may each use '456'");
    }

    [Fact]
    public async Task Duplicate_External_Id_For_One_Partner_Is_Blocked_By_The_Filtered_Index()
    {
        using var factory = fixture.CreateFactory();
        var external = External();

        await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .CreatePartnerCustomerAsync(703, external, Contact(), "corr"));

        // Straight at the context, bypassing the port's race handling, so the index is what is
        // being tested rather than the retry around it.
        var act = async () => await Scoped(factory, async services =>
        {
            var db = services.GetRequiredService<EwaysVNextDbContext>();

            db.Customers.Add(Customer.CreatePartnerLinked(
                9_000_001, 703, external, EwaysVNext.Domain.ValueObjects.Email.Create("b@x.com"),
                null, null, DateTimeOffset.UtcNow));

            return await db.SaveChangesAsync();
        });

        var thrown = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
        var postgres = thrown.InnerException.Should().BeOfType<PostgresException>().Which;

        postgres.SqlState.Should().Be("23505");
        postgres.ConstraintName.Should().Be("ux_customers_partner_external");
    }

    [Fact]
    public async Task Same_Email_On_Two_Customers_Is_Allowed()
    {
        using var factory = fixture.CreateFactory();
        var shared = $"shared-{Guid.NewGuid():N}@x.com";

        var partnerLinked = await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .CreatePartnerCustomerAsync(704, External(), Contact(email: shared), "corr"));

        var website = await Scoped(factory, services =>
            services.GetRequiredService<IWebsiteCustomerPort>()
                .EnsureFromIdentityAsync(Guid.NewGuid(), shared, emailVerified: true));

        partnerLinked.IsSuccess.Should().BeTrue();
        website.IsSuccess.Should().BeTrue(
            "a person buying directly and the same person ordered for by a partner are two "
            + "records by design; this system does not merge them");
    }

    [Fact]
    public async Task Concurrent_Creates_For_One_External_Id_Produce_One_Customer()
    {
        using var factory = fixture.CreateFactory();
        var external = External();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Scoped(factory, services =>
                services.GetRequiredService<ICustomerResolutionPort>()
                    .CreatePartnerCustomerAsync(705, external, Contact(), "corr"))));

        attempts.Should().OnlyContain(result => result.IsSuccess,
            "the loser of the race reads the winner's row rather than failing");

        attempts.Select(result => result.Value!.CustomerId).Distinct().Should().ContainSingle();

        var rows = await Scoped(factory, services =>
            services.GetRequiredService<EwaysVNextDbContext>().Customers.AsNoTracking()
                .CountAsync(customer => customer.PartnerId == 705 && customer.ExternalCustomerId == external));

        rows.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_First_Calls_From_One_Identity_Produce_One_Website_Customer()
    {
        using var factory = fixture.CreateFactory();
        var subject = Guid.NewGuid();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            Scoped(factory, services =>
                services.GetRequiredService<IWebsiteCustomerPort>()
                    .EnsureFromIdentityAsync(subject, "racer@x.com", emailVerified: true))));

        attempts.Should().OnlyContain(result => result.IsSuccess);
        attempts.Select(result => result.Value!.CustomerId).Distinct().Should().ContainSingle(
            "several tabs authenticating at once is the ordinary case, not an edge one");
    }

    [Fact]
    public async Task Sync_Persists_Contact_History_With_Partner_And_Correlation()
    {
        using var factory = fixture.CreateFactory();
        var external = External();

        var created = await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .CreatePartnerCustomerAsync(706, external, Contact(), "corr-create"));

        var customerId = created.Value!.CustomerId;

        var synced = await Scoped(factory, services =>
            services.GetRequiredService<ICustomerResolutionPort>()
                .SyncPartnerContactAsync(
                    customerId, 706, new ContactInput("changed@x.com", null, null), "corr-sync"));

        synced.IsSuccess.Should().BeTrue();

        var changes = await Scoped(factory, services =>
            services.GetRequiredService<EwaysVNextDbContext>().Customers.AsNoTracking()
                .Where(customer => customer.Id == customerId)
                .SelectMany(customer => customer.ContactChanges)
                .ToListAsync());

        changes.Should().ContainSingle(change =>
            change.Field == ContactField.Email
            && change.OldValue == "a@x.com"
            && change.NewValue == "changed@x.com"
            && change.SourcePartnerId == 706
            && change.CorrelationId == "corr-sync");
    }

    [Fact]
    public async Task Check_Constraint_Refuses_A_Website_Row_With_A_PartnerId()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO customer.customers
                (id, kind, status, identity_user_id, partner_id, external_customer_id,
                 email_verified, created_at)
            VALUES (9000002, 'Website', 'Active', gen_random_uuid(), 99, 'x', false, now())
            """,
            connection);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514",
                "a row carrying both kinds' keys is reachable by one lookup and invisible to the other");
    }

    [Fact]
    public async Task Check_Constraint_Refuses_A_PartnerLinked_Row_Without_An_External_Id()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO customer.customers
                (id, kind, status, identity_user_id, partner_id, external_customer_id,
                 email_verified, created_at)
            VALUES (9000003, 'PartnerLinked', 'Active', NULL, 99, NULL, false, now())
            """,
            connection);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("23514");
    }
}
