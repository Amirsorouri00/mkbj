using EwaysVNext.Outcomes;

namespace EwaysVNext.Modules.Sales.Application;

/// <summary>
/// Every refusal Sales can express, in one place.
/// </summary>
/// <remarks>
/// <para>
/// <c>Retryable</c> is a promise to the caller that sending the same request again is safe and
/// might work. Only two failures here qualify: a composition that ran out of time, and a batch
/// that hit its deadline. Everything else is a decision that will not change on a retry, and
/// marking one retryable would invite a partner to hammer it.
/// <c>SalesErrorsTests.Retryable_Errors_Are_Exactly_Timeout_And_Batch_Deadline</c> holds that.
/// </para>
/// <para>
/// The distinction between 422 and 400 is deliberate throughout: 400 means the request was not
/// understood, 422 means it was understood and refused. A partner that asks for an amount no
/// basket can total sent a perfectly well-formed request.
/// </para>
/// </remarks>
public static class SalesErrors
{
    /// <summary>The request did not parse. The caller must change what it sends.</summary>
    public static Error ValidationFailed(string message) => new("VALIDATION_FAILED", message, 400);

    /// <summary>Outside the seller's configured minimum or maximum order value.</summary>
    public static readonly Error AmountOutOfRange = new(
        "AMOUNT_OUT_OF_RANGE",
        "That amount is outside the range this seller accepts for an order.",
        422);

    /// <summary>
    /// The engine finished and found no basket totalling the amount exactly.
    /// </summary>
    /// <remarks>
    /// Not retryable: the same amount against the same catalogue will fail the same way. The
    /// partner's move is a different amount, not a second attempt.
    /// </remarks>
    public static readonly Error ExactCompositionUnavailable = new(
        "EXACT_COMPOSITION_UNAVAILABLE",
        "No basket of active products totals that amount exactly.",
        422);

    /// <summary>
    /// The engine ran out of its budget before deciding either way.
    /// </summary>
    /// <remarks>
    /// Retryable, and 503 rather than 422, because this says nothing about the amount — a second
    /// attempt gets a different seed and may well succeed.
    /// </remarks>
    public static readonly Error CompositionTimeout = new(
        "COMPOSITION_TIMEOUT",
        "Composing a basket for that amount took too long. Please try again.",
        503,
        Retryable: true);

    /// <summary>
    /// The same fingerprint arrived twice with different content.
    /// </summary>
    /// <remarks>
    /// A retry of an identical request returns the original order; this is the other case, where
    /// a caller reused an idempotency key for a different order. Answering it with either order
    /// would be wrong.
    /// </remarks>
    public static readonly Error OrderIdempotencyConflict = new(
        "ORDER_IDEMPOTENCY_CONFLICT",
        "That idempotency key was already used for a different order.",
        409);

    /// <summary>An invoice cannot be issued without the buyer fields the law requires.</summary>
    public static readonly Error InvoiceBuyerFieldsMissing = new(
        "INVOICE_BUYER_FIELDS_MISSING",
        "The buyer's details are incomplete, so an invoice cannot be issued.",
        422);

    public static readonly Error CustomerContactRequired = new(
        "CUSTOMER_CONTACT_REQUIRED",
        "A contact email or phone number is required for this customer.",
        422);

    /// <summary>
    /// A price moved between the quote the caller was shown and the attempt to use it.
    /// </summary>
    /// <remarks>
    /// Not retryable, even though a retry would probably succeed: the caller must see the new
    /// price and decide again. Retrying silently is how someone is charged an amount they never
    /// agreed to.
    /// </remarks>
    public static readonly Error PriceChanged = new(
        "PRICE_CHANGED",
        "A price changed since this was quoted.",
        409);

    public static readonly Error NotFound = new("NOT_FOUND", "No such order.", 404);

    public static readonly Error InsufficientScope = new(
        "INSUFFICIENT_SCOPE", "This API key does not grant that.", 403);

    /// <summary>The batch hit its wall-clock deadline with items still unprocessed.</summary>
    public static readonly Error BatchDeadlineExceeded = new(
        "BATCH_DEADLINE_EXCEEDED",
        "The batch took too long. Items that were not processed can be resubmitted.",
        503,
        Retryable: true);

    /// <summary>The order is not in a state where that is allowed.</summary>
    public static readonly Error OrderStateConflict = new(
        "ORDER_STATE_CONFLICT", "The order is not in a state that allows this.", 409);

    public static readonly Error RefundExceedsRefundable = new(
        "REFUND_EXCEEDS_REFUNDABLE",
        "That refund is more than what remains refundable on this order.",
        422);

    public static readonly Error EmailNotVerified = new(
        "EMAIL_NOT_VERIFIED", "Verify your email address before ordering.", 403);

    public static readonly Error CustomerSuspended = new(
        "CUSTOMER_SUSPENDED", "This account cannot place orders.", 403);

    /// <summary>
    /// Something failed that the caller cannot act on.
    /// </summary>
    /// <remarks>
    /// Not retryable, deliberately: an unknown failure is not known to be transient, and
    /// promising otherwise turns one broken request into a retry storm.
    /// </remarks>
    public static Error Internal(string message) => new("INTERNAL_SALES", message, 500);

    /// <summary>
    /// Recorded on an order whose contact details could not be pushed back to Customer.
    /// </summary>
    /// <remarks>
    /// A warning, never an error: the order is sold, invoiced and paid for. Failing it because a
    /// phone number did not sync would be refusing money over bookkeeping.
    /// </remarks>
    public const string ContactSyncFailedWarning = "CONTACT_SYNC_FAILED";
}
