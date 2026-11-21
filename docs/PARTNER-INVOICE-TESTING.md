# Partner amount-to-invoice testing

This implements the partner sales path in [PRD sections 6–12](product-scope/ecommerce_mvp_prd.md),
the [approved design](superpowers/specs/2026-10-28-ecommerce-mvp-design.md), and the
[Sales implementation plan](superpowers/plans/2026-10-28-ecommerce-mvp/phase-4-5-sales.md).
The transaction boundary remains [ADR-0018](adr/0018-sales-aggregate-owns-order-lifecycle.md).

## Try it in Scalar

1. Start the development dependencies and API using [ONBOARDING](ONBOARDING.md). Apply migrations
   with `--migrate`, then run `--seed` for a priced demo catalogue.
2. Open **http://localhost:8608/scalar/v1**. Under **Partner Orders**, open
   **POST /api/v1/orders** and choose **Test Request**.
3. Select **PartnerApiKey** authentication and paste your `pk_...` key, without a `Bearer ` prefix.
   The key needs `orders:create`, `orders:read`, `invoices:read` and `customers:write`.
4. Send the populated example. It returns **201** with the order and invoice.
5. Save `data.id` and `data.invoice.id`. IDs are decimal **strings**; keep them as strings in
   JavaScript. Converting a Snowflake ID to `Number` loses precision.

```json
{
  "external_order_id": "scalar-order-001",
  "external_customer_id": "scalar-customer-001",
  "amount": "500.00",
  "currency": "USD",
  "customer": {
    "email": "buyer@example.com",
    "phone": "+6591234567",
    "full_name": "Alex Tan"
  }
}
```

For another purchase, change `external_order_id`. To reuse the customer, keep
`external_customer_id`; its customer object may be empty. An amount the live, priced catalogue
cannot compose is refused; products are never invented.

To issue a key, sign into Scalar with the Backoffice security scheme as a super admin, create a
partner with **POST /api/v1/admin/partners**, then call
**POST /api/v1/admin/partners/{id}/keys**. Use the exact ID from the creation `Location` header.
The plaintext key is returned once. The locally provisioned review key lives only in ignored
`.env.partner-demo`; it is never embedded in public API documentation.

## Retrieve and inspect

| Action | Endpoint / page |
|---|---|
| Read the order | `GET /api/v1/orders/{id}` |
| Recover by your reference | `GET /api/v1/orders/ext:scalar-order-001` |
| Read invoice JSON | `GET /api/v1/invoices/{invoiceId}` |
| Download partner PDF | `GET /api/v1/invoices/{invoiceId}/pdf` |
| Admin submission history | http://localhost:5173/partner-requests |
| Admin orders and documents | http://localhost:5173/orders |

The partner PDF endpoint redirects to a private, five-minute storage link. Follow that redirect;
do not send the partner key to object storage. The admin endpoint serves an authenticated PDF
attachment directly, so browser downloads do not depend on object-storage CORS settings. PDFs
already rendered and stored keep their original bytes; the new layout applies at first rendering.

In **API requests**, search by an exact external order ID or correlation ID. Open a submission
to see its input, response, status, replay flag, linked orders and invoice downloads.
`X-Request-Id` is the receipt ID. A rejected duplicate may link to the previously created order;
that does not mean the rejected request created it. Malformed authenticated submissions are also
recorded. Authorization headers and keys are not recorded. This history contains customer data
and requires staff `orders.manage`.

## Reliability and idempotency

- `(partner_id, external_order_id)` is the unique idempotency key. Identical retries return
  **200**, the same order/invoice IDs and `Idempotent-Replay: true`. A changed amount, currency,
  or customer identity returns **409**. `Idempotency-Key` is deliberately ignored.
- Each accepted authenticated input is persisted before processing. Its response is recorded
  before being sent. Inputs larger than 1 MiB are rejected before persistence.
- Order lines, payment, fulfillment and invoice snapshot are one order aggregate and commit in
  **one EF SaveChanges transaction**. No manual cross-module transaction is used.
- Customer creation is a separate idempotent operation required by module boundaries. A later
  order failure can leave a customer without an order; a retry resolves that customer rather
  than duplicating it. Bulk orders commit independently per item.
- A process interruption or response-journal failure can leave an **Outcome not recorded**
  receipt. Admin still resolves a committed order by partner and external reference. The caller
  must retry the same external order ID or GET by external reference. HTTP cannot prove that a
  remote caller received a response.
- Receipt, order and response recording are separate database commits, not a distributed
  transaction. Receipt failure prevents processing; order atomicity is the aggregate transaction.
- Receipt time is passed into order creation and frozen into the invoice. The PDF displays that
  date in Singapore time (UTC+08:00), even when downloaded later.
- PDF rendering and storage are lazy. Storage failure delays download; it cannot remove the
  committed order or change the invoice date, lines, quantities or total.

## Verification

Integration tests cover exact totals, price bounds, duplicate and concurrent retries, tenant
isolation, bulk partial results, recorded input/output, interrupted-response recovery, string IDs,
Scalar examples and authenticated PDF downloads. PDF tests parse rendered output, check
deterministic bytes, multi-page headers, totals and Singapore dates. Visual review covers short
and long invoices plus desktop and mobile admin pages. See
[TESTING-GUARANTEES](TESTING-GUARANTEES.md) for suite limitations.
