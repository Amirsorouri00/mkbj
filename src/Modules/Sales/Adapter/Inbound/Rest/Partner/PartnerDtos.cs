using System.Text.Json;
using System.Text.RegularExpressions;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Modules.Sales.QueryApplication.Partner;
using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Adapter.Inbound.Rest.Partner;

/// <remarks>
/// <c>Amount</c> is a <see cref="JsonElement"/> rather than a string so that a JSON <b>number</b>
/// is refused with a clear message instead of being silently bound as text or rejected by the
/// serializer with one. A number is the mistake a client most often makes here, and it is the
/// one that would otherwise lose a cent.
/// </remarks>
public sealed record CreatePartnerOrderRequest(
    string? ExternalOrderId,
    string? ExternalCustomerId,
    JsonElement Amount,
    string? Currency,
    PartnerCustomerRequest? Customer);

public sealed record PartnerCustomerRequest(string? Email, string? Phone, string? FullName);

public sealed record MoneyDto(string Amount, string Currency);

public sealed record PartnerInvoiceResponse(
    long Id, string Number, JsonElement Json, string PdfUrl);

public sealed record PartnerOrderLineResponse(
    int LineNo,
    long VariantId,
    long ProductId,
    string Sku,
    string ProductTitle,
    string BrandName,
    IReadOnlyList<string> CategoryPath,
    IReadOnlyDictionary<string, string> Attributes,
    int Quantity,
    MoneyDto UnitPrice,
    MoneyDto LineTotal,
    MoneyDto ReferencePrice,
    int DeviationBps,
    string PriceSource);

public sealed record PartnerOrderResponse(
    long Id,
    string Number,
    string ExternalOrderId,
    long CustomerId,
    string ExternalCustomerId,
    MoneyDto Total,
    MoneyDto Subtotal,
    MoneyDto Tax,
    OrderStatusDto Status,
    IReadOnlyList<PartnerOrderLineResponse> Lines,
    PartnerInvoiceResponse? Invoice,
    DateTimeOffset CreatedAt,
    string CorrelationId,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Turns a validated request into a command, and an order into the partner's wire shape.
/// </summary>
/// <remarks>
/// Every amount leaves as a two-decimal string with its currency beside it. A bare number would
/// be a double to most clients, and a partner reconciling against its own ledger would find
/// cents that do not add up.
/// </remarks>
internal static partial class PartnerContracts
{
    private const string PdfRoutePrefix = "/api/v1/invoices/";
    private const string PdfRouteSuffix = "/pdf";

    public static Result<(string ExternalOrderId, string ExternalCustomerId, long AmountMinor, string Currency, PartnerContact Contact)>
        Validate(CreatePartnerOrderRequest request)
    {
        if (request.Customer is null) return Invalid("customer is required.");

        if (request.Amount.ValueKind != JsonValueKind.String)
        {
            return Invalid(
                "amount must be a string with exactly two decimal places, for example \"500.00\".");
        }

        if (!AmountText.TryParseMinor(request.Amount.GetString(), out var amountMinor))
            return Invalid($"'{request.Amount.GetString()}' is not an amount.");

        if (request.Currency is null || !CurrencyPattern().IsMatch(request.Currency))
            return Invalid("currency must be three upper-case letters.");

        if (request.ExternalOrderId is null || !IdPattern().IsMatch(request.ExternalOrderId))
            return Invalid("external_order_id must be 1-100 characters of A-Z, a-z, 0-9, . _ : or -.");

        if (request.ExternalCustomerId is null || !IdPattern().IsMatch(request.ExternalCustomerId))
            return Invalid("external_customer_id must be 1-100 characters of A-Z, a-z, 0-9, . _ : or -.");

        return Result.Success((
            request.ExternalOrderId,
            request.ExternalCustomerId,
            amountMinor,
            request.Currency,
            new PartnerContact(
                request.Customer.Email, request.Customer.Phone, request.Customer.FullName)));
    }

    public static PartnerOrderResponse ToResponse(
        PartnerOrderDto order, string correlationId, IReadOnlyList<string> warnings) =>
        new(
            order.Id,
            order.Number,
            order.ExternalOrderId,
            order.CustomerId,
            order.ExternalCustomerId,
            Money(order.TotalMinor, order.Currency),
            Money(order.SubtotalMinor, order.Currency),
            Money(order.TaxMinor, order.Currency),
            order.Status,
            [.. order.Lines.Select(line => new PartnerOrderLineResponse(
                line.LineNo,
                line.VariantId,
                line.ProductId,
                line.Sku,
                line.ProductTitle,
                line.BrandName,
                line.CategoryPath,
                line.Attributes,
                line.Quantity,
                Money(line.UnitPriceMinor, order.Currency),
                Money(line.LineTotalMinor, order.Currency),
                Money(line.ReferencePriceMinor, order.Currency),
                line.DeviationBps,
                line.PriceSource))],
            order.Invoice is null
                ? null
                : new PartnerInvoiceResponse(
                    order.Invoice.Id,
                    order.Invoice.Number,
                    order.Invoice.Content,
                    PdfRoutePrefix
                        + order.Invoice.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + PdfRouteSuffix),
            order.CreatedAt,
            correlationId,
            warnings);

    private static MoneyDto Money(long minor, string currency) =>
        new(AmountText.Format(minor), currency);

    private static Result<(string, string, long, string, PartnerContact)> Invalid(string message) =>
        Result.Failure<(string, string, long, string, PartnerContact)>(
            Application.SalesErrors.ValidationFailed(message));

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex CurrencyPattern();

    /// <remarks>
    /// Deliberately narrow. These ids end up in URLs, log lines and invoice numbers, and a
    /// partner that can put a slash or a space in one has made every later reference ambiguous.
    /// </remarks>
    [GeneratedRegex(@"^[A-Za-z0-9._:\-]{1,100}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
