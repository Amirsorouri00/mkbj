# ADR-0018: One `Order` Aggregate Owns Payment, Fulfillment, Documents and Refunds

## Status
Accepted — 2026-10-28.

Supersedes [ADR-0015](0015-payment-gateway.md) in full: the gateway module it describes is gone
(ADR-0022), and the MVP has no payment gateway at all.

## Context

PRD §6 is unambiguous about a partner order: *"atomically persist order, items, partner-reported
payment record, automatic fulfillment assertion and commercial invoice"*, and §8.1 adds that if any
part fails, **no order, payment or invoice may exist**. A partner must never receive a 500 and then
discover an order was created anyway, nor an order with no invoice.

That requirement collides with two standing rules:

- **Rule 7** — one `SaveChangesAsync` per operation, and never one spanning two aggregates whose
  invariants depend on each other.
- **Rule 8** — retries are enabled, so no user-initiated transaction. An EF execution strategy
  throws on a transaction it did not begin (ADR-0017 D8).

With Order, Payment and Invoice as separate modules, the only ways to satisfy the PRD would be a
distributed transaction (impossible here), a saga with compensation (ADR-0011 removed the toolkit,
and compensation leaves a window where the partner's view is wrong), or breaking rule 8.

## Decision

**Payment status, fulfillment status, issued documents and refunds are part of the `Order`
aggregate, in one `Sales` module. Atomicity comes from aggregate design, not from a transaction.**

`Order.CreatePartnerOrder(...)` builds the whole thing — lines, the partner-reported
`PaymentRecord`, the system-automatic fulfillment assertion and the `Commercial` document — and the
service saves **once**. EF wraps a single `SaveChangesAsync` in its own transaction, so the PRD's
"atomically persist" is satisfied by the storage engine, with no explicit transaction anywhere and
both rules intact.

This is not a convenience. The four things genuinely share invariants:

- A document may only be issued for an order whose buyer fields are complete.
- `PaymentStatus` may only move to `Refunded` when `Σ refunds ≤ total`, which requires the lines.
- A credit note must reference the commercial invoice of the same order.
- The fulfillment assertion is meaningless apart from the order it asserts about.

Rule 7's test is whether invariants span the parts. Here they do, so one aggregate is the correct
boundary — not a workaround for it.

### What stays outside

`Cart` is its own aggregate: it has no invariant shared with a placed order and is discarded at
checkout. Customers, catalog items, prices and seller settings stay in their own modules and are
reached through gateway ports, their values **snapshotted** into the order at creation. A later
price or product change cannot alter a historical order (PRD §5).

### The one non-atomic edge, stated plainly

Creating a **new** partner customer is a separate write in the Customer module, and it happens
before the order save (spec §10.3 step 5). If the order save then fails, that customer row
persists with no order.

This is deliberate and harmless: the customer is keyed by `(partner_id, external_customer_id)`, so
a retry resolves to the same row rather than creating a second. The alternative — one save across
Customer and Sales — would put two modules' aggregates in one transaction, which rule 7 forbids and
which would couple the two modules' schemas forever.

**Contact synchronisation for an existing customer happens after the order is committed**, and a
failure there does not undo the order: the response returns 201 with a `CONTACT_SYNC_FAILED`
warning, and the invoice uses the contact values the partner supplied in the request. An order is
not worth losing because an address line did not update.

### Website orders use the same aggregate

`CheckoutWebsite` creates `PendingPayment` plus a pro forma; `ConfirmExternalPayment` moves to
Paid/AdminConfirmed, Fulfilled, and issues the commercial invoice — again in one save. The four
status dimensions (order, payment, fulfillment, document) are independent fields, as PRD §8.3
requires, not a single enum.

## Consequences

- **The `Sales` module is large.** It owns carts, orders, the composition engine, documents and
  refunds. That is the price of putting the consistency boundary where the invariants are.
- **No saga, no compensation, no outbox** on the order path. Nothing to reconcile after a crash:
  either the single save committed or nothing did.
- **A failed composition writes nothing**, because composition is a pure function that runs before
  any write (spec §10.3 step 4).
- **Idempotency is a unique index**, not coordination: `UNIQUE(partner_id, external_order_id)
  WHERE channel = 'PartnerApi'`. A concurrent duplicate loses on the index, reloads, and returns
  the existing order.
- **The invoice PDF is rendered lazily** from the document's stored JSON (spec §5.6), so object
  storage is not on the order-write path and a MinIO outage cannot fail an order.
- **ADR-0015 is superseded.** Its provider selection, failover, callback and reversal policies
  describe a module that no longer exists.
