using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;

namespace EwaysVNext.Modules.Sales.Application.Services;

/// <summary>
/// The seller is not GST-registered: no tax is charged, and the document says so.
/// </summary>
/// <remarks>
/// Saying "Not GST registered" is not boilerplate. A buyer who sees a zero tax line without it
/// may assume the supply is zero-rated and try to reclaim input tax that was never charged.
/// </remarks>
public sealed class NonGstTaxPolicy : ITaxPolicy
{
    public TaxAssessment Assess(long subtotalMinor, string currency) =>
        new(0, DocumentWording.NotGstRegistered);
}
