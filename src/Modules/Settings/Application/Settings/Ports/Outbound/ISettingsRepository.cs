using EwaysVNext.Modules.Settings.Application.Settings.Domain;

namespace EwaysVNext.Modules.Settings.Application.Settings.Ports.Outbound;

/// <summary>
/// Loads the three singletons. Each returns null only if the seed has not run, which is a
/// deployment fault rather than a state the application should carry on in.
/// </summary>
public interface ISettingsRepository
{
    Task<SellerProfile?> GetSellerAsync(bool tracking, CancellationToken ct = default);

    Task<PaymentInstruction?> GetPaymentInstructionAsync(bool tracking, CancellationToken ct = default);

    Task<CommerceLimits?> GetCommerceLimitsAsync(bool tracking, CancellationToken ct = default);
}
