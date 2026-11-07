using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;

namespace EwaysVNext.Modules.Sales.QueryApplication;

/// <summary>
/// The one way an order's status is described to any reader.
/// </summary>
/// <remarks>
/// Shared by the partner, shop and admin read sides deliberately. Three copies of this would
/// drift, and the first sign of the drift would be two surfaces disagreeing about whether an
/// order is invoiced.
/// </remarks>
internal static class OrderStatusMapping
{
    /// <summary>
    /// Describes an order, deriving the document status from the documents themselves.
    /// </summary>
    /// <remarks>
    /// Derived here as on the aggregate, so the stored row and the read model can never
    /// disagree about what has been issued.
    /// </remarks>
    public static OrderStatusDto Describe(
        OrderStatus order,
        PaymentStatus payment,
        PaymentSource? paymentSource,
        FulfillmentStatus fulfillment,
        FulfillmentSource? fulfillmentSource,
        IEnumerable<DocumentType> documentTypes)
    {
        var types = documentTypes.ToList();

        var documents =
            types.Contains(DocumentType.CreditNote) ? DocumentStatus.CreditNoted
            : types.Contains(DocumentType.Commercial) ? DocumentStatus.Invoiced
            : types.Contains(DocumentType.ProForma) ? DocumentStatus.ProFormaIssued
            : DocumentStatus.None;

        return new OrderStatusDto(
            order.ToString(),
            payment.ToString(),
            paymentSource?.ToString(),
            fulfillment.ToString(),
            fulfillmentSource?.ToString(),
            DocumentWording.FulfillmentStatement,
            documents.ToString());
    }
}
