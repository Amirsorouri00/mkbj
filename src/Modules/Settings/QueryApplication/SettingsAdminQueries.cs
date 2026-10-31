using System.Globalization;

using EwaysVNext.Database;
using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using EwaysVNext.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EwaysVNext.Modules.Settings.QueryApplication;

public interface ISettingsAdminQueries
{
    Task<SellerView> GetSellerAsync(CancellationToken ct = default);

    /// <summary>Newest first, with the active one flagged.</summary>
    Task<IReadOnlyList<PaymentInstructionVersionView>> GetPaymentInstructionsAsync(CancellationToken ct = default);

    Task<CommerceLimitsView> GetCommerceLimitsAsync(CancellationToken ct = default);
}

public sealed class SettingsAdminQueries(EwaysVNextDbContext db) : ISettingsAdminQueries
{
    public async Task<SellerView> GetSellerAsync(CancellationToken ct = default)
    {
        var seller = await db.SellerProfiles.AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.Id == SettingsDefaults.SingletonId, ct)
            ?? throw Missing("seller profile");

        return new SellerView(
            seller.LegalName, seller.Uen, seller.Address, seller.Email,
            seller.GstRegistered, seller.InvoiceFooter, seller.RequiredBuyerFields);
    }

    public async Task<IReadOnlyList<PaymentInstructionVersionView>> GetPaymentInstructionsAsync(
        CancellationToken ct = default)
    {
        var instruction = await db.PaymentInstructions.AsNoTracking()
            .Include(record => record.Versions)
            .SingleOrDefaultAsync(record => record.Id == SettingsDefaults.SingletonId, ct)
            ?? throw Missing("payment instruction");

        return [.. instruction.Versions
            .OrderByDescending(version => version.Version)
            .Select(version => new PaymentInstructionVersionView(
                version.Id,
                version.Version,
                version.Text,
                version.Version == instruction.ActiveVersion,
                version.CreatedBy,
                version.CreatedAt))];
    }

    public async Task<CommerceLimitsView> GetCommerceLimitsAsync(CancellationToken ct = default)
    {
        var limits = await db.CommerceLimits.AsNoTracking()
            .SingleOrDefaultAsync(record => record.Id == SettingsDefaults.SingletonId, ct)
            ?? throw Missing("commerce limits");

        return new CommerceLimitsView(
            Usd(limits.MinUsdMinor),
            Usd(limits.MaxUsdMinor),
            limits.BulkDefaultMax,
            limits.CompositionDeadlineMs,
            limits.CompositionMaxLines);
    }

    /// <summary>Always two decimals, so "25" and "25.00" never both appear for the same amount.</summary>
    private static string Usd(long minor) =>
        MinorUnits.FromMinor(minor).ToString("F2", CultureInfo.InvariantCulture);

    private static InvalidOperationException Missing(string what) =>
        new($"The {what} singleton is missing. Run the migrations: Settings rows are seeded, not created on demand.");
}
