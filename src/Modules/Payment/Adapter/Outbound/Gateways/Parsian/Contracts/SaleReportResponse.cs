namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

internal sealed record SaleReportResponse(
    short Status,
    string? Message,
    IReadOnlyList<SaleReportRecord> Records);
