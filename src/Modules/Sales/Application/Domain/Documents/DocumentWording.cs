namespace EwaysVNext.Modules.Sales.Application.Domain.Documents;

/// <summary>What kind of document this is.</summary>
public enum DocumentType
{
    /// <summary>Issued once money is due and the sale is final.</summary>
    Commercial = 1,

    /// <summary>Issued before payment, so the buyer knows what to pay and where.</summary>
    ProForma = 2,

    /// <summary>Issued against an earlier invoice when money goes back.</summary>
    CreditNote = 3,
}

/// <summary>
/// The fixed phrases that appear on documents, in one place because each is a statement with
/// legal weight.
/// </summary>
/// <remarks>
/// <para>
/// <b>No document here may be titled as a GST document.</b> In Singapore the phrase combining
/// "tax" and "invoice" is reserved for a GST-registered supplier, and this seller is not one —
/// using it would misrepresent the seller's status to the buyer and to IRAS.
/// <c>SalesArchitectureTests</c> scans the whole module's source for that phrase and fails the
/// build on it, which is why this paragraph goes the long way round rather than quoting it.
/// </para>
/// <para>
/// The fulfilment statement is equally deliberate. The system knows what an operator marked as
/// fulfilled; it does not know what arrived. Saying so on the document is the difference between
/// a record and a claim.
/// </para>
/// </remarks>
public static class DocumentWording
{
    public const string FulfillmentStatement =
        "Fulfillment is an operational assertion, not verified proof of delivery";

    public const string NotGstRegistered = "Not GST registered";

    public static string TitleFor(DocumentType type) => type switch
    {
        DocumentType.Commercial => "Invoice",
        DocumentType.ProForma => "Pro Forma Invoice",
        DocumentType.CreditNote => "Credit Note",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown document type."),
    };
}
