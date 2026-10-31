using EwaysVNext.Core.Application;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Settings.Application.Settings.Ports.Inbound;

public sealed record UpdateSellerCommand(
    string LegalName,
    string Uen,
    string Address,
    string Email,
    bool GstRegistered,
    string InvoiceFooter,
    IReadOnlyList<string> RequiredBuyerFields);

public sealed record UpdateCommerceLimitsCommand(
    long MinUsdMinor,
    long MaxUsdMinor,
    int BulkDefaultMax,
    int CompositionDeadlineMs,
    int CompositionMaxLines);

/// <summary>
/// Changing settings is super-admin work: these values decide what appears on every invoice, what
/// a customer is told to do to pay, and the bounds every order is checked against. Each call is
/// audited in the same save as the change it describes.
/// </summary>
public interface ISettingsAdminService
{
    Task<Result> UpdateSellerAsync(UpdateSellerCommand command, AdminActor actor, CancellationToken ct = default);

    /// <summary>Returns the new version number.</summary>
    Task<Result<int>> PublishPaymentInstructionAsync(string text, AdminActor actor, CancellationToken ct = default);

    Task<Result> ActivatePaymentInstructionAsync(int version, AdminActor actor, CancellationToken ct = default);

    Task<Result> UpdateCommerceLimitsAsync(UpdateCommerceLimitsCommand command, AdminActor actor, CancellationToken ct = default);
}
