# Getting Started

## Base URL

| Environment | URL |
|-------------|-----|
| Local dev | `http://localhost:5000` |
| Docker stack | `http://localhost:5000` (via `docker compose`) |

The OpenAPI spec is served at `/openapi/v1.json` in **all environments** (anonymous).
Scalar (the interactive API browser) is available at `/scalar/v1` in **Development only**.

## Authentication

Three authentication schemes exist. Every endpoint requires exactly one (or none).

### 1. Keycloak OAuth 2.0 — Shop realm

For website customers. Uses Authorization Code + PKCE.

| Parameter | Value |
|-----------|-------|
| Realm | `Shop` |
| Client ID | configured in `appsettings.json` → `Keycloak:Realms:Shop:ClientId` |
| Auth URL | `{Keycloak:ExternalUrl}/realms/Shop/protocol/openid-connect/auth` |
| Token URL | `{Keycloak:ExternalUrl}/realms/Shop/protocol/openid-connect/token` |
| Scopes | `openid` |
| PKCE | SHA-256 (required) |

Header: `Authorization: Bearer <access_token>`

### 2. Keycloak OAuth 2.0 — Backoffice realm

For admin panel users. Same flow as Shop but against the `Backoffice` realm.

| Parameter | Value |
|-----------|-------|
| Realm | `Backoffice` |
| Client ID | configured in `appsettings.json` → `Keycloak:Realms:Backoffice:ClientId` |
| Auth URL | `{Keycloak:ExternalUrl}/realms/Backoffice/protocol/openid-connect/auth` |
| Token URL | `{Keycloak:ExternalUrl}/realms/Backoffice/protocol/openid-connect/token` |
| Scopes | `openid` |
| PKCE | SHA-256 (required) |

Roles: `staff` (most admin operations) or `super_admin` (partners, settings, FX, refunds).

Header: `Authorization: Bearer <access_token>`

### 3. Partner API Key

For B2B integrations. Keys are issued by an admin and scoped.

Header: `Authorization: Bearer pk_<prefix>_<secret>`

The key is detected by its `pk_` prefix — no scheme negotiation needed.

Scopes: `orders.create`, `orders.read`, `customers.read`, `invoices.read`.

## Scalar — Interactive API Testing

In Development mode, open **http://localhost:5000/scalar/v1** in your browser.

Scalar is pre-configured with:
- OAuth flows for both Keycloak realms (click "Authorize" and sign in)
- Partner API Key scheme (paste `pk_<prefix>_<secret>` as the bearer token)
- All REST endpoints grouped by tag

To get a Backoffice token for Scalar quickly (if you have the Keycloak admin password):

```bash
eval "$(bash tools/smoke/backoffice-token.sh http://127.0.0.1:8081 admin admin)"
# Prints TOKEN=... — paste it into Scalar's bearer field
```

## Common Request/Response Patterns

### Error Body

Every non-2xx response uses this shape:

```json
{
  "code": "VARIANT_NOT_FOUND",
  "message": "Variant 123456 does not exist.",
  "retryable": false,
  "correlationId": "abc-123"
}
```

`code` is a stable machine-readable string. `retryable` tells you if the same request might succeed later.

### Correlation ID

Every response carries `X-Correlation-Id`. Send it on requests too — if you include it, the API echoes it back; if you don't, one is generated. Use it for debugging and support.

### Pagination

List endpoints accept:
- `page` (1-based, default 1)
- `pageSize` (default varies, max 100)

Response:

```json
{
  "items": [...],
  "page": 1,
  "pageSize": 25,
  "totalCount": 142
}
```

Some endpoints also support Gridify filter/sort syntax via `filter` and `orderBy` query params.

### Money

All monetary values are **minor units** (cents/pence) as `long`, paired with a 3-letter ISO currency code. For example, SGD 12.50 is `{ "amountMinor": 1250, "currency": "SGD" }`.

Never use floating point for money on the frontend. Convert at the display layer only.

## Rate Limiting

- **Anonymous callers**: 100 requests/minute per IP (configurable via `RateLimiting__AnonymousPermitPerMinute`)
- **Partner callers**: per-partner limit set on the key itself

A `429 Too Many Requests` response includes `Retry-After` header.

## CORS

Configure `Host:AllowedCorsOrigins` in `appsettings.json` (array of allowed origins). In dev with no config, all origins are allowed.

## Health Checks

| Endpoint | Purpose |
|----------|---------|
| `GET /health/live` | Liveness — always 200 if process is up |
| `GET /health/ready` | Readiness — checks tagged dependencies |
| `GET /health` | Full health |
