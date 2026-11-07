namespace EwaysVNext.Modules.Sales.Application.Domain.Orders;

/// <summary>Which of the two sales paths an order came down.</summary>
/// <remarks>
/// They differ in almost everything that matters — who pays, when, what document is issued and
/// who confirms receipt — so the channel is recorded rather than inferred from whether a partner
/// id happens to be present.
/// </remarks>
public enum OrderChannel
{
    Website = 1,
    PartnerApi = 2,
}

public enum OrderStatus
{
    PendingPayment = 1,
    Completed = 2,
    Cancelled = 3,
}

public enum PaymentStatus
{
    Unpaid = 1,
    Paid = 2,
    PartiallyRefunded = 3,
    Refunded = 4,
}

/// <summary>Who says the money arrived.</summary>
/// <remarks>
/// A partner's own report and an admin's confirmation are different kinds of evidence, and an
/// audit needs to tell them apart: one is the counterparty's word, the other is the seller's
/// own check against a bank statement.
/// </remarks>
public enum PaymentSource
{
    PartnerReported = 1,
    AdminConfirmed = 2,
}

public enum FulfillmentStatus
{
    Unfulfilled = 1,
    Fulfilled = 2,
}

/// <remarks>
/// Only one source today, and it is named for what it is: the system marked it, nobody checked.
/// The document says as much — see <c>DocumentWording.FulfillmentStatement</c>.
/// </remarks>
public enum FulfillmentSource
{
    SystemAutomatic = 1,
}

/// <summary>How far an order has got through its paperwork.</summary>
/// <remarks>Derived from the documents actually issued, never stored.</remarks>
public enum DocumentStatus
{
    None = 0,
    ProFormaIssued = 1,
    Invoiced = 2,
    CreditNoted = 3,
}

/// <summary>Where a line's unit price came from, kept so an invoice can be explained.</summary>
public enum LinePriceSource
{
    Override = 1,
    Fx = 2,
    Base = 3,
}
