using System.Text.Json;
using System.Text.Json.Serialization;

namespace EwaysVNext.Modules.Sales.Application.Domain.Documents;

public sealed record SellerBlock(
    string LegalName, string Uen, string Address, string Email, string Footer);

/// <remarks>
/// Every field but the id is optional, because what an invoice must carry depends on the
/// channel: a website buyer has an email, a partner's customer may have only a company name and
/// a registration number. <see cref="BuyerFieldRequirements"/> decides what a given document
/// needs; this just carries whatever is known.
/// </remarks>
public sealed record BuyerBlock(
    long CustomerId,
    string? ExternalCustomerId,
    string? FullName,
    string? CompanyName,
    string? Email,
    string? Phone,
    string? BillingAddress,
    string? RegistrationNumber);

public sealed record DocumentLine(
    int LineNo, string Sku, string Description, int Quantity, long UnitPriceMinor, long LineTotalMinor);

/// <remarks>
/// The version is carried, not just the text: the instruction a buyer was given is part of what
/// they were told to do, and it must stay readable after the seller changes their bank details.
/// </remarks>
public sealed record PaymentInstructionBlock(long InstructionId, int Version, string Text);

/// <summary>
/// Everything printed on one document, frozen at the moment it was issued.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot, not a view. The seller's address, the buyer's details, the prices and the payment
/// instruction are copied in rather than referenced, so reprinting an invoice from 2026 in 2029
/// produces the 2026 document — not the 2026 numbers wrapped in the 2029 company's letterhead.
/// </para>
/// <para>
/// Equality is by value including the lines, so two snapshots that print the same are the same.
/// The record's generated equality would compare the list by reference, which would make every
/// round-tripped snapshot unequal to itself.
/// </para>
/// </remarks>
public sealed record DocumentContent(
    string Title,
    string DocumentType,
    string DocumentNumber,
    DateTimeOffset IssuedAt,
    string OrderNumber,
    string Channel,
    string? ExternalOrderId,
    SellerBlock Seller,
    BuyerBlock Buyer,
    IReadOnlyList<DocumentLine> Lines,
    string Currency,
    long SubtotalMinor,
    long TaxMinor,
    long TotalMinor,
    string TaxStatement,
    string FulfillmentStatement,
    string PaymentStatus,
    string? PaymentSource,
    PaymentInstructionBlock? PaymentInstruction,
    string? RelatedDocumentNumber,
    long? RefundAmountMinor,
    string? RefundReason)
{
    public bool Equals(DocumentContent? other) =>
        other is not null
        && Title == other.Title
        && DocumentType == other.DocumentType
        && DocumentNumber == other.DocumentNumber
        && IssuedAt == other.IssuedAt
        && OrderNumber == other.OrderNumber
        && Channel == other.Channel
        && ExternalOrderId == other.ExternalOrderId
        && Seller == other.Seller
        && Buyer == other.Buyer
        && Lines.SequenceEqual(other.Lines)
        && Currency == other.Currency
        && SubtotalMinor == other.SubtotalMinor
        && TaxMinor == other.TaxMinor
        && TotalMinor == other.TotalMinor
        && TaxStatement == other.TaxStatement
        && FulfillmentStatement == other.FulfillmentStatement
        && PaymentStatus == other.PaymentStatus
        && PaymentSource == other.PaymentSource
        && PaymentInstruction == other.PaymentInstruction
        && RelatedDocumentNumber == other.RelatedDocumentNumber
        && RefundAmountMinor == other.RefundAmountMinor
        && RefundReason == other.RefundReason;

    public override int GetHashCode() =>
        HashCode.Combine(DocumentNumber, OrderNumber, IssuedAt, TotalMinor, Currency, Lines.Count);
}

/// <summary>
/// How a <see cref="DocumentContent"/> is stored.
/// </summary>
/// <remarks>
/// The settings are fixed rather than taken from an ambient default because this JSON is
/// persisted: a snapshot written today has to deserialize years from now, and a global
/// serializer setting changed in the meantime would silently change what an old invoice says.
/// Writing it twice must also produce identical bytes, so a stored document can be hashed.
/// </remarks>
public static class DocumentContentJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };
}
