using EwaysVNext.Modules.Sales.Application.Domain.Documents;

namespace EwaysVNext.Modules.Sales.Application.Ports.Outbound;

/// <summary>
/// The seller's tax position, as a port rather than a constant.
/// </summary>
/// <remarks>
/// Today the seller is not GST-registered and every assessment is zero. That will change the day
/// turnover crosses the registration threshold, and when it does the change belongs in one
/// implementation — not spread across every place a document is built.
/// </remarks>
public interface ITaxPolicy
{
    TaxAssessment Assess(long subtotalMinor, string currency);
}
