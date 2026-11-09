# Partner API Reference

Partners authenticate with API keys: `Authorization: Bearer pk_<prefix>_<secret>`

Each key carries scopes that gate which endpoints it can access. Keys are issued by a `super_admin` through the admin panel.

---

## Orders

### Create — scope `orders.create`

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/v1/orders` | Create a single order |
| POST | `/api/v1/orders/bulk` | Create orders in bulk |

**Single order body (amount-driven composition):**
```json
{
  "amount": 50000,
  "currency": "SGD",
  "buyerName": "Acme Corp",
  "buyerEmail": "purchasing@acme.com"
}
```

The system composes a real basket of active SKUs totalling the requested amount exactly (within ±5% per unit price). The order is paid-by-partner and invoiced immediately.

**Bulk order body:**
```json
{
  "orders": [
    { "amount": 50000, "currency": "SGD", "buyerName": "Acme Corp", "buyerEmail": "a@acme.com" },
    { "amount": 25000, "currency": "SGD", "buyerName": "Beta Inc", "buyerEmail": "b@beta.com" }
  ]
}
```

### Read — scope `orders.read`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/orders` | List partner's orders (paginated) |
| GET | `/api/v1/orders/{id}` | Order detail |

---

## Customers

Scope: `customers.read`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/customers/{id}/orders` | Orders for a specific buyer |

---

## Invoices

Scope: `invoices.read`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/invoices/{id}` | Invoice detail |
| GET | `/api/v1/invoices/{id}/pdf` | Download invoice PDF |
| GET | `/api/v1/invoices/search` | Search invoices |

---

## Response Codes

| Code | Meaning |
|------|---------|
| 200 | Success |
| 201 | Created (order placed) |
| 400 | Bad request (validation, composition impossible) |
| 401 | Missing or invalid API key |
| 403 | Key does not have the required scope |
| 404 | Resource not found |
| 409 | Conflict (duplicate request) |
| 429 | Rate limit exceeded — check `Retry-After` header |

---

## Composition Notes

The composition engine is pure and deterministic: given the same inputs and catalog state, it produces the same basket. The seed is persisted with the order, so it can be reproduced from its inputs.

If composition cannot satisfy the requested amount (not enough sellable, priced variants), a 400 is returned with error code `COMPOSITION_IMPOSSIBLE`.

The ±5% band means a variant priced at 1000 minor units can appear in a basket at 950–1050, but never outside that range. The total always matches the requested amount exactly.
