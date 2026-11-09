# Admin API Reference

All admin endpoints are under `/api/v1/admin/` and require a **Backoffice** Keycloak token.

Auth: `Authorization: Bearer <backoffice_access_token>`

Two roles exist: `staff` (most operations) and `super_admin` (partners, settings, FX, refunds). Each endpoint also checks a fine-grained permission (editable in the RBAC system).

---

## Dashboard & 360 Views

Policy: `admin:staff` + `orders.manage` or `customers.manage`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/dashboard` | Dashboard summary (counts, recent activity) |
| GET | `/api/v1/admin/customers/{id}/360` | Customer 360 view (profile + recent orders) |
| GET | `/api/v1/admin/orders/{id}/360` | Order 360 view (full detail + customer + invoice) |

---

## Customer Management

Policy: `admin:staff` + `customers.manage`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/customers` | List customers (paginated, filterable) |
| GET | `/api/v1/admin/customers/{id}` | Customer detail |
| PUT | `/api/v1/admin/customers/{id}` | Update customer |

---

## Catalog Management

Policy: `admin:staff` + `catalog.manage`

### Brands

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/catalog/brands` | List brands |
| POST | `/api/v1/admin/catalog/brands` | Create brand |
| PUT | `/api/v1/admin/catalog/brands/{id}` | Update brand |

### Categories

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/catalog/categories` | List categories |
| POST | `/api/v1/admin/catalog/categories` | Create category |
| PUT | `/api/v1/admin/catalog/categories/{id}` | Update category |
| DELETE | `/api/v1/admin/catalog/categories/{id}` | Delete category |

### Attribute Blueprints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/catalog/attributes` | List attribute blueprints |
| POST | `/api/v1/admin/catalog/attributes` | Create attribute blueprint |
| PUT | `/api/v1/admin/catalog/attributes/{id}` | Update attribute blueprint |

### Products

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/catalog/products` | List products (paginated) |
| POST | `/api/v1/admin/catalog/products` | Create product |
| GET | `/api/v1/admin/catalog/products/{id}` | Product detail |
| PUT | `/api/v1/admin/catalog/products/{id}` | Update product |
| POST | `/api/v1/admin/catalog/products/{id}/publish` | Publish product |
| POST | `/api/v1/admin/catalog/products/{id}/unpublish` | Unpublish product |

### Variants

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/catalog/products/{id}/variants` | List variants |
| POST | `/api/v1/admin/catalog/products/{id}/variants` | Create variant |
| PUT | `/api/v1/admin/catalog/variants/{id}` | Update variant |
| POST | `/api/v1/admin/catalog/variants/{id}/identifiers` | Set identifiers (SKU, GTIN, MPN) |
| POST | `/api/v1/admin/catalog/variants/{id}/media` | Upload media |
| DELETE | `/api/v1/admin/catalog/variants/{id}/media/{mediaId}` | Remove media |

---

## Pricing

### Staff operations — `admin:staff` + `catalog.manage`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/pricing/variants/{variantId}` | Get variant price info |
| PUT | `/api/v1/admin/pricing/variants/{variantId}/price` | Set reference price |
| PUT | `/api/v1/admin/pricing/variants/{variantId}/overrides/{currency}` | Set currency override |
| DELETE | `/api/v1/admin/pricing/variants/{variantId}/overrides/{currency}` | Remove currency override |
| GET | `/api/v1/admin/pricing/fx/rates` | List FX rates |
| GET | `/api/v1/admin/pricing/fx/overrides` | List FX overrides |
| GET | `/api/v1/admin/pricing/currencies` | List all currencies |

### Super operations — `admin:super` + `fx.manage`

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/v1/admin/pricing/fx/overrides` | Create FX override |
| DELETE | `/api/v1/admin/pricing/fx/overrides/{base}/{quote}` | Remove FX override |
| PUT | `/api/v1/admin/pricing/currencies/{code}` | Enable/disable currency |
| POST | `/api/v1/admin/pricing/fx/refresh` | Trigger FX rate refresh |

---

## Order Management

Policy: `admin:staff` + `orders.manage`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/orders` | List orders (paginated, filterable) |
| GET | `/api/v1/admin/orders/{id}` | Order detail |
| POST | `/api/v1/admin/orders/{id}/confirm-payment` | Confirm payment received |
| POST | `/api/v1/admin/orders/{id}/issue-invoice` | Issue commercial invoice |
| POST | `/api/v1/admin/orders/{id}/cancel` | Cancel order |

### Refunds — `admin:super` + `refunds.manage`

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/v1/admin/orders/{id}/refund` | Issue refund |

---

## Partner Management

Policy: `admin:super` + `partners.manage`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/partners` | List partners |
| POST | `/api/v1/admin/partners` | Create partner |
| GET | `/api/v1/admin/partners/{id}` | Partner detail |
| PUT | `/api/v1/admin/partners/{id}` | Update partner |
| POST | `/api/v1/admin/partners/{id}/keys` | Issue API key |
| DELETE | `/api/v1/admin/partners/{id}/keys/{keyId}` | Revoke API key |
| GET | `/api/v1/admin/partners/{id}/usage` | API usage log |

---

## Settings

Policy: `admin:super` + `settings.manage`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/settings/seller` | Seller profile |
| PUT | `/api/v1/admin/settings/seller` | Update seller profile |
| GET | `/api/v1/admin/settings/payment-instructions` | Current payment instructions |
| PUT | `/api/v1/admin/settings/payment-instructions` | Update payment instructions (versioned) |
| GET | `/api/v1/admin/settings/limits` | Commerce limits |
| PUT | `/api/v1/admin/settings/limits` | Update commerce limits |

---

## Imports (Sourcing)

### Staff operations — `admin:staff` + `imports.manage`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/imports/jobs` | List import jobs |
| GET | `/api/v1/admin/imports/jobs/{id}` | Job detail |
| POST | `/api/v1/admin/imports/jobs` | Create import job (file upload or URL) |
| POST | `/api/v1/admin/imports/jobs/{id}/start` | Start processing |
| POST | `/api/v1/admin/imports/jobs/{id}/pause` | Pause processing |
| POST | `/api/v1/admin/imports/jobs/{id}/resume` | Resume processing |
| POST | `/api/v1/admin/imports/jobs/{id}/cancel` | Cancel job |
| PUT | `/api/v1/admin/imports/jobs/{id}/mapping` | Set column mapping |
| GET | `/api/v1/admin/imports/records` | List staged records (paginated) |
| GET | `/api/v1/admin/imports/records/{id}` | Record detail |
| POST | `/api/v1/admin/imports/records/{id}/approve` | Approve record |
| POST | `/api/v1/admin/imports/records/{id}/merge` | Merge into existing product |
| POST | `/api/v1/admin/imports/records/{id}/create-new` | Create as new product |
| POST | `/api/v1/admin/imports/records/{id}/reject` | Reject record |

### Source rights policies — `admin:super` + `settings.manage`

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/v1/admin/imports/policies` | Create rights policy |
| PUT | `/api/v1/admin/imports/policies/{id}` | Update rights policy |

**Import job creation body (file upload):**
```json
{
  "format": "Csv",
  "mode": "File"
}
```
The file is uploaded as `multipart/form-data`. Supported formats: `Csv`, `Json` (XLSX reader is not yet built).

**Column mapping body:**
```json
{
  "mapping": {
    "Product Name": "name",
    "Brand": "brand",
    "Price (SGD)": "price",
    "SKU": "sku",
    "Color": "attr:color"
  }
}
```

Canonical target columns: `name`, `brand`, `description`, `sku`, `gtin`, `mpn`, `price`, `currency`, `category`, `image_url`, `source_url`, `source_domain`, `source_id`. Attributes use `attr:<key>` prefix. Unmapped columns use `ignore`.

---

## Audit

Policy: `admin:staff`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/v1/admin/audit/verify` | Verify audit trail integrity |

---

## GraphQL (Admin surface)

Admin mutations and queries are also available via GraphQL at `/graphql` with per-resolver auth. See [graphql-api.md](graphql-api.md).
