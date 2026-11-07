using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EwaysVNext.Core.Application;
using EwaysVNext.Domain.Common;
using EwaysVNext.Modules.Sales.Application.Composition;
using EwaysVNext.Modules.Sales.Application.Domain.Documents;
using EwaysVNext.Modules.Sales.Application.Domain.Orders;
using EwaysVNext.Modules.Sales.Application.Domain.Telemetry;
using EwaysVNext.Modules.Sales.Application.Money;
using EwaysVNext.Modules.Sales.Application.Ports.Inbound;
using EwaysVNext.Modules.Sales.Application.Ports.Outbound;
using EwaysVNext.Outcomes;
using Microsoft.Extensions.Logging;

namespace EwaysVNext.Modules.Sales.Application.Services;

/// <summary>
/// Turns a partner's requested amount into a real, paid, invoiced order.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order of the steps is the design.</b> Nothing is written until a basket exists: a
/// composition that fails must leave no customer, no order and no invoice behind, because the
/// partner will retry and a half-made record would make the retry mean something different.
/// Everything that only reads — idempotency, limits, the customer lookup, the buyer-field check
/// — happens first, so the common refusals cost nothing.
/// </para>
/// <para>
/// Reads happen in a fixed order too: the cheapest refusal first. An amount outside the seller's
/// limits is settled before any customer is looked up, and a missing buyer field before any
/// composition is attempted.
/// </para>
/// </remarks>
internal sealed class PartnerOrderService(
    IOrderRepository orders,
    ICandidatePoolGateway pool,
    ISalesPricingGateway pricing,
    ISalesCustomerGateway customers,
    ISalesSettingsGateway settings,
    IDocumentNumberAllocator numbers,
    ICompositionAttemptRecorder attempts,
    ICompositionBudgetFactory budgets,
    ICompositionSeedSource seeds,
    ITaxPolicy taxPolicy,
    LiveRequoter requoter,
    IIdGenerator ids,
    IClock clock,
    ILogger<PartnerOrderService> logger) : IPartnerOrderService
{
    /// <summary>
    /// How many times a basket may be recomposed when the live re-quote rejects it.
    /// </summary>
    /// <remarks>
    /// Three, because each retry means the shop changed under the request. A fourth would be
    /// chasing a catalogue that is moving faster than the order can be placed, and the partner is
    /// better told to try again than kept waiting.
    /// </remarks>
    private const int MaxRequoteAttempts = 3;

    public async Task<Result<PartnerOrderCreated>> CreateAsync(
        PartnerPrincipal principal,
        CreatePartnerOrderCommand command,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(command);

        // Read once: every snapshot, rate and timestamp on this order must agree.
        var now = clock.UtcNow;

        // 1. Idempotency, before anything else — a retry must be cheap.
        var existing = await orders.FindPartnerOrderKeyAsync(
            principal.PartnerId, command.ExternalOrderId, ct);

        var fingerprint = RequestFingerprint.Compute(
            command.ExternalCustomerId, command.AmountMinor, command.Currency);

        if (existing is not null) return Replay(existing, fingerprint);

        // 2. What the seller allows, and what it can be composed from.
        var limits = await settings.GetCommerceLimitsAsync(ct);
        var (seller, requiredBuyerFields, _) = await settings.GetSellerAsync(ct);

        var candidates = await pool.GetAsync(command.Currency, now, ct);
        if (candidates.IsFailure) return Result.Failure<PartnerOrderCreated>(candidates.Error!);

        var usd = await pricing.ToUsdAsync(command.AmountMinor, command.Currency, now, ct);
        if (usd.IsFailure) return Result.Failure<PartnerOrderCreated>(usd.Error!);

        if (usd.Value!.UsdMinor < limits.MinUsdMinor || usd.Value.UsdMinor > limits.MaxUsdMinor)
            return Result.Failure<PartnerOrderCreated>(SalesErrors.AmountOutOfRange);

        // 3. Who is being billed — looked up, never written yet.
        var buyerResult = await ResolveBuyerAsync(principal, command, requiredBuyerFields, ct);
        if (buyerResult.IsFailure) return Result.Failure<PartnerOrderCreated>(buyerResult.Error!);

        var resolved = buyerResult.Value!;

        // 4. Compose, re-quoting against live data before anything is sold.
        var composed = await ComposeAsync(
            principal, command, limits, candidates.Value!, usd.Value.UsdMinor, now, ct);

        if (composed.IsFailure) return Result.Failure<PartnerOrderCreated>(composed.Error!);

        // 5. Only now does anything get written.
        var customerId = resolved.CustomerId;
        if (customerId is null)
        {
            var created = await customers.CreatePartnerCustomerAsync(
                principal.PartnerId, command.ExternalCustomerId, resolved.Supplied!, correlationId, ct);

            if (created.IsFailure) return Result.Failure<PartnerOrderCreated>(created.Error!);

            customerId = created.Value!.CustomerId;
        }

        var saved = await SaveAsync(
            principal, command, correlationId, fingerprint, customerId.Value,
            resolved.Buyer with { CustomerId = customerId.Value },
            seller, requiredBuyerFields, composed.Value!, usd.Value.UsdMinor, now, ct);

        if (saved.IsFailure) return Result.Failure<PartnerOrderCreated>(saved.Error!);

        // 6. Push the partner's latest contact details back, after the sale is safe.
        var warnings = resolved.SyncNeeded
            ? await SyncContactAsync(principal, customerId.Value, resolved.Supplied!, correlationId, ct)
            : [];

        return Result.Success(new PartnerOrderCreated(saved.Value, Replayed: false, warnings));
    }

    public Task<IReadOnlyList<BulkItemOutcome>> CreateBulkAsync(
        PartnerPrincipal principal, IReadOnlyList<BulkItem> items, string correlationId,
        CancellationToken ct = default) =>
        throw new NotImplementedException("Bulk orders arrive in task 16.");

    private static Result<PartnerOrderCreated> Replay(PartnerOrderKey existing, string fingerprint) =>
        // Same request, same answer. A different one under the same key is the partner's bug,
        // and answering with either order would be wrong.
        existing.RequestFingerprint == fingerprint
            ? Result.Success(new PartnerOrderCreated(existing.OrderId, Replayed: true, []))
            : Result.Failure<PartnerOrderCreated>(SalesErrors.OrderIdempotencyConflict);

    private sealed record ResolvedBuyer(
        long? CustomerId, BuyerBlock Buyer, PartnerContact? Supplied, bool SyncNeeded);

    /// <summary>
    /// Works out who the invoice is to, and whether writing anything would need a scope the key
    /// does not have — without writing anything.
    /// </summary>
    private async Task<Result<ResolvedBuyer>> ResolveBuyerAsync(
        PartnerPrincipal principal,
        CreatePartnerOrderCommand command,
        IReadOnlyList<string> requiredBuyerFields,
        CancellationToken ct)
    {
        var supplied = command.Contact ?? new PartnerContact(null, null, null);
        var found = await customers.FindPartnerCustomerAsync(
            principal.PartnerId, command.ExternalCustomerId, ct);

        BuyerBlock buyer;
        long? customerId;
        bool syncNeeded;

        if (found is null)
        {
            if (!principal.Has(PartnerScopes.CustomersWrite))
                return Result.Failure<ResolvedBuyer>(SalesErrors.InsufficientScope);

            // A customer this system has never seen needs both, because the seller may have to
            // reach the buyer about an order placed on their behalf by somebody else.
            if (string.IsNullOrWhiteSpace(supplied.Email) || string.IsNullOrWhiteSpace(supplied.Phone))
                return Result.Failure<ResolvedBuyer>(SalesErrors.CustomerContactRequired);

            customerId = null;
            syncNeeded = false;
            buyer = new BuyerBlock(
                CustomerId: 0,
                command.ExternalCustomerId,
                supplied.FullName,
                CompanyName: null,
                supplied.Email,
                supplied.Phone,
                BillingAddress: null,
                RegistrationNumber: null);
        }
        else
        {
            var party = await customers.GetInvoicePartyAsync(found.CustomerId, ct);
            if (party.IsFailure) return Result.Failure<ResolvedBuyer>(party.Error!);

            customerId = found.CustomerId;

            // Only a supplied value that actually differs is a change. A partner that sends the
            // same details on every order is not asking to rewrite the record each time.
            syncNeeded = Differs(supplied.Email, party.Value!.Email)
                || Differs(supplied.Phone, party.Value.Phone)
                || Differs(supplied.FullName, party.Value.FullName);

            if (syncNeeded && !principal.Has(PartnerScopes.CustomersWrite))
                return Result.Failure<ResolvedBuyer>(SalesErrors.InsufficientScope);

            // The invoice uses what the partner supplied for this order, falling back to what is
            // stored. An order placed with a new address should be invoiced to that address even
            // if the sync later fails.
            buyer = new BuyerBlock(
                party.Value.CustomerId,
                command.ExternalCustomerId,
                supplied.FullName ?? party.Value.FullName,
                party.Value.CompanyName,
                supplied.Email ?? party.Value.Email,
                supplied.Phone ?? party.Value.Phone,
                party.Value.BillingAddress,
                party.Value.RegistrationNumber);
        }

        var missing = BuyerFieldRequirements.Missing(requiredBuyerFields, buyer);
        if (missing.Count > 0)
            return Result.Failure<ResolvedBuyer>(SalesErrors.InvoiceBuyerFieldsMissing);

        return Result.Success(new ResolvedBuyer(customerId, buyer, supplied, syncNeeded));
    }

    private static bool Differs(string? supplied, string? stored) =>
        supplied is not null && !string.Equals(supplied, stored, StringComparison.Ordinal);

    private sealed record ComposedBasket(
        IReadOnlyList<OrderLineInput> Lines, CompositionTraceInput Trace);

    /// <summary>
    /// Composes a basket and re-checks it against live data, retrying when the shop moved.
    /// </summary>
    private async Task<Result<ComposedBasket>> ComposeAsync(
        PartnerPrincipal principal,
        CreatePartnerOrderCommand command,
        SalesCommerceLimits limits,
        CandidatePool candidates,
        long usdMinor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var budget = budgets.Start(TimeSpan.FromMilliseconds(limits.CompositionDeadlineMs));
        var seed = seeds.NextSeed();
        var started = Stopwatch.GetTimestamp();

        var available = candidates.Candidates;
        var excluded = new HashSet<long>();
        var notes = new List<string>();

        for (var attempt = 0; attempt < MaxRequoteAttempts; attempt++)
        {
            var plan = CompositionEngine.Compose(
                command.AmountMinor,
                available,
                new CompositionOptions(limits.CompositionMaxLines, usdMinor),
                seed + attempt,
                budget);

            if (plan.IsFailure)
            {
                await RecordAsync(principal, command, started, plan.Error!.Code, 0, attempt, notes, seed, ct);
                return Result.Failure<ComposedBasket>(plan.Error);
            }

            var requoted = await requoter.ResolveAsync(plan.Value!.Lines, command.Currency, now, ct);

            if (requoted.DroppedVariantIds.Count == 0)
            {
                notes.AddRange(plan.Value.Stats.AttemptSummaries);

                await RecordAsync(
                    principal, command, started, null, requoted.Lines.Count, attempt, notes, seed, ct);

                return Result.Success(new ComposedBasket(
                    requoted.Lines,
                    new CompositionTraceInput(
                        seed + attempt,
                        plan.Value.Stats.StrategyVersion,
                        plan.Value.Stats.Iterations,
                        (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                        plan.Value.Stats.CandidatePoolSize,
                        JsonSerializer.Serialize(notes))));
            }

            // The shop moved under the composition. Forget the cached pool — it is now known to
            // be wrong — and reload before trying again.
            notes.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"requote:dropped={requoted.DroppedVariantIds.Count}"));

            foreach (var variantId in requoted.DroppedVariantIds) excluded.Add(variantId);

            pool.Invalidate(command.Currency);

            var reloaded = await pool.GetAsync(command.Currency, now, ct);
            if (reloaded.IsFailure) return Result.Failure<ComposedBasket>(reloaded.Error!);

            available = [.. reloaded.Value!.Candidates.Where(c => !excluded.Contains(c.VariantId))];
        }

        await RecordAsync(
            principal, command, started, SalesErrors.ExactCompositionUnavailable.Code, 0,
            MaxRequoteAttempts, notes, seed, ct);

        return Result.Failure<ComposedBasket>(SalesErrors.ExactCompositionUnavailable);
    }

    private async Task<Result<long>> SaveAsync(
        PartnerPrincipal principal,
        CreatePartnerOrderCommand command,
        string correlationId,
        string fingerprint,
        long customerId,
        BuyerBlock buyer,
        SellerBlock seller,
        IReadOnlyList<string> requiredBuyerFields,
        ComposedBasket basket,
        long usdMinor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var orderNumber = await numbers.NextAsync(DocumentNumberKind.Order, ct);
        var invoiceNumber = await numbers.NextAsync(DocumentNumberKind.Commercial, ct);
        var orderId = ids.Generate();

        var lines = basket.Lines
            .Select(line => line with { LineId = ids.Generate() })
            .ToList();

        Order order;
        try
        {
            order = Order.CreatePartnerOrder(
                new PartnerOrderHeader(
                    orderId,
                    orderNumber,
                    principal.PartnerId,
                    command.ExternalOrderId,
                    command.ExternalCustomerId,
                    fingerprint,
                    customerId,
                    command.Currency,
                    command.AmountMinor,
                    usdMinor,
                    correlationId),
                lines,
                basket.Trace,
                new IssuedDocumentRef(ids.Generate(), invoiceNumber),
                ids.Generate(),
                seller,
                requiredBuyerFields,
                buyer,
                taxPolicy.Assess(command.AmountMinor, command.Currency),
                now);
        }
        catch (DomainException exception)
        {
            // The aggregate refusing here means this service let something through that it
            // should have caught, with one exception: the buyer-field rule, which the caller can
            // actually act on.
            return exception.Code == "INVOICE_BUYER_FIELDS_MISSING"
                ? Result.Failure<long>(SalesErrors.InvoiceBuyerFieldsMissing)
                : Result.Failure<long>(SalesErrors.Internal(exception.Message));
        }

        if (await orders.TryInsertPartnerOrderAsync(order, ct)) return Result.Success(orderId);

        // Lost the race against another copy of this request. Whatever that one created is the
        // answer, decided the same way step 1 decides it.
        var winner = await orders.FindPartnerOrderKeyAsync(
            principal.PartnerId, command.ExternalOrderId, ct);

        return winner is null
            ? Result.Failure<long>(SalesErrors.Internal("The order could not be stored."))
            : winner.RequestFingerprint == fingerprint
                ? Result.Success(winner.OrderId)
                : Result.Failure<long>(SalesErrors.OrderIdempotencyConflict);
    }

    /// <summary>
    /// Pushes contact details back to Customer, and never undoes the order if that fails.
    /// </summary>
    /// <remarks>
    /// The order is sold, paid and invoiced by this point. Refusing it because a phone number
    /// did not propagate would be turning down money over bookkeeping — so the failure becomes a
    /// warning on the response and a counter the seller can watch.
    /// </remarks>
    private async Task<IReadOnlyList<string>> SyncContactAsync(
        PartnerPrincipal principal,
        long customerId,
        PartnerContact supplied,
        string correlationId,
        CancellationToken ct)
    {
        try
        {
            var synced = await customers.SyncPartnerContactAsync(
                customerId, principal.PartnerId, supplied, correlationId, ct);

            if (synced.IsSuccess) return [];

            logger.LogWarning(
                "Could not sync contact details for customer {CustomerId}: {Code}",
                customerId, synced.Error!.Code);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception, "Could not sync contact details for customer {CustomerId}.", customerId);
        }

        SalesMetrics.CustomerContactSyncFailures.Add(1);

        return [SalesErrors.ContactSyncFailedWarning];
    }

    private async Task RecordAsync(
        PartnerPrincipal principal,
        CreatePartnerOrderCommand command,
        long started,
        string? errorCode,
        int lines,
        int recompositions,
        IReadOnlyList<string> notes,
        long seed,
        CancellationToken ct)
    {
        var elapsed = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        SalesMetrics.CompositionDurationMs.Record(elapsed);

        _ = notes;

        await attempts.RecordAsync(
            new CompositionAttemptRecord(
                clock.UtcNow,
                principal.PartnerId,
                command.Currency,
                command.AmountMinor,
                Succeeded: errorCode is null,
                errorCode,
                elapsed,
                lines,
                Iterations: 0,
                CandidatePoolSize: 0,
                seed,
                CompositionEngine.StrategyVersion,
                recompositions,
                OrderId: null),
            ct);
    }
}
