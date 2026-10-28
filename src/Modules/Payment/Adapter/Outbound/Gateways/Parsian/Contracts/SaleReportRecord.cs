namespace EwaysVNext.Modules.Payment.Adapter.Outbound.Gateways.Parsian.Contracts;

internal sealed record SaleReportRecord(
    long Amount,
    long OrderId,
    long Token,
    long RRN,
    DateTime? RequestDateTime,
    short? PGWStatusId);
