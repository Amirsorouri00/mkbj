using EwaysVNext.Modules.Settings.Application.Settings.Domain;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;
using EwaysVNext.Modules.Settings.Application.Settings.Ports.Outbound;
using Microsoft.Extensions.Options;

namespace EwaysVNext.Modules.Settings.Application.Settings.Services;

public sealed class SettingsPortService(
    ISettingsRepository repository, IOptions<FxAgeOptions> fxOptions) : ISettingsPort
{
    public async Task<SellerSnapshot> GetSellerAsync(CancellationToken ct = default)
    {
        var seller = await repository.GetSellerAsync(tracking: false, ct) ?? throw Missing("seller profile");

        return new SellerSnapshot(
            seller.LegalName, seller.Uen, seller.Address, seller.Email,
            seller.GstRegistered, seller.InvoiceFooter, seller.RequiredBuyerFields);
    }

    public async Task<PaymentInstructionSnapshot> GetActivePaymentInstructionAsync(CancellationToken ct = default)
    {
        var instruction = await repository.GetPaymentInstructionAsync(tracking: false, ct)
            ?? throw Missing("payment instruction");

        var active = instruction.Active;

        return new PaymentInstructionSnapshot(active.Id, active.Version, active.Text);
    }

    public async Task<CommerceLimitsSnapshot> GetCommerceLimitsAsync(CancellationToken ct = default)
    {
        var limits = await repository.GetCommerceLimitsAsync(tracking: false, ct)
            ?? throw Missing("commerce limits");

        return new CommerceLimitsSnapshot(
            limits.MinUsdMinor, limits.MaxUsdMinor, limits.BulkDefaultMax,
            limits.CompositionDeadlineMs, limits.CompositionMaxLines,
            fxOptions.Value.MaxRateAge);
    }

    /// <summary>
    /// Throws rather than returning a default. These rows are seeded by the migration, so their
    /// absence means the database was not provisioned — and quietly substituting defaults would
    /// put a placeholder legal name on a real invoice.
    /// </summary>
    private static InvalidOperationException Missing(string what) =>
        new($"The {what} singleton is missing. Run the migrations: Settings rows are seeded, not created on demand.");
}
