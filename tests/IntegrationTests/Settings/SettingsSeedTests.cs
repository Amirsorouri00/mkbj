using AwesomeAssertions;
using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EwaysVNext.IntegrationTests.Settings;

/// <summary>
/// The settings singletons are created by the migration, not on demand, so the only thing worth
/// proving here is that a freshly migrated database already holds exactly what the spec says —
/// and that a second row cannot be inserted beside them.
/// </summary>
[Collection(PostgresSuite.Name)]
public sealed class SettingsSeedTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Seeded_Defaults_Match_Spec()
    {
        // Its own database, because this asserts what a FRESH one contains. On the shared
        // database the settings admin tests change these very rows, so the result depended on
        // execution order rather than on the migration.
        using var factory = await fixture.CreateFreshDatabaseFactoryAsync();
        using var scope = factory.Services.CreateScope();

        var port = scope.ServiceProvider.GetRequiredService<ISettingsPort>();

        (await port.GetCommerceLimitsAsync()).Should().Be(
            new CommerceLimitsSnapshot(1_000, 100_000_000, 100, 8000, 200, TimeSpan.FromHours(72)));

        var seller = await port.GetSellerAsync();
        seller.GstRegistered.Should().BeFalse();
        seller.RequiredBuyerFields.Should().Equal(SettingsDefaults.RequiredBuyerFieldsEntry);
        seller.LegalName.Should().Be(SettingsDefaults.LegalName,
            "the placeholder is what makes skipping the go-live configuration visible");

        var instruction = await port.GetActivePaymentInstructionAsync();
        instruction.Version.Should().Be(1);
        instruction.Text.Should().Be(SettingsDefaults.InitialPaymentInstructionText);
    }

    [Theory]
    [InlineData("settings.seller_profile",
        "INSERT INTO settings.seller_profile (id, legal_name, uen, address, email, gst_registered, invoice_footer, required_buyer_fields) "
        + "VALUES (2, 'x', 'x', 'x', 'x', false, '', ARRAY['email'])")]
    [InlineData("settings.commerce_limits",
        "INSERT INTO settings.commerce_limits (id, min_usd_minor, max_usd_minor, bulk_default_max, composition_deadline_ms, composition_max_lines) "
        + "VALUES (2, 1000, 2000, 10, 8000, 10)")]
    [InlineData("settings.payment_instruction",
        "INSERT INTO settings.payment_instruction (id, active_version) VALUES (2, 1)")]
    public async Task Second_Singleton_Row_Is_Rejected_By_Check_Constraint(string table, string insert)
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(insert, connection);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23514",
                $"{table} must hold one row; a second would be silently ignored by every read");
    }

    [Fact]
    public async Task A_Duplicate_Version_Number_Is_Rejected()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "INSERT INTO settings.payment_instruction_versions (id, payment_instruction_id, version, text, created_at) "
            + "VALUES (999, 1, 1, 'duplicate', now())",
            connection);

        var act = async () => await command.ExecuteNonQueryAsync();

        (await act.Should().ThrowAsync<PostgresException>())
            .Which.SqlState.Should().Be("23505",
                "two rows claiming version 1 make 'which wording was this invoice issued under' unanswerable");
    }
}
