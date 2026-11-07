namespace EwaysVNext.Modules.Sales.Application.Domain.Documents;

/// <summary>
/// How much tax is due on a document, and what it says about why.
/// </summary>
/// <remarks>
/// A domain value, not a port concern: the figure and the statement are both printed on the
/// document, and the aggregate needs them to build one. <c>ITaxPolicy</c>, which decides what
/// they are, is the port — and it lives in <c>Ports.Outbound</c> because the answer depends on
/// the seller's registration status rather than on anything the order knows.
/// </remarks>
public sealed record TaxAssessment(long TaxMinor, string Statement);
