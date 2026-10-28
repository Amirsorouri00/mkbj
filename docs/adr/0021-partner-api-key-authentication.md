# ADR-0021: Partner API Keys — Format, Hashing, Scopes and Tenancy

## Status
Accepted — 2026-10-28.

Complements [ADR-0017](0017-postgres-and-minio.md). Carries forward the multi-replica caveat from
[ADR-0010](0010-kubernetes-deployment-strategy.md) for the in-process rate limiter.

## Context

B2B partners authenticate machine-to-machine (PRD §3, §9). Keycloak serves humans — the storefront
and the backoffice — and giving partners realm accounts would mean OIDC client credentials,
per-partner clients, and Keycloak as a hard dependency of every partner request.

Partners need something simpler and stricter: a key they can rotate, scopes that limit what it can
do, a rate limit per partner, and an absolute guarantee that one partner can never see another's
data.

## Decision

### D1 — Format: `pk_<8-char prefix>_<32-byte base62 secret>`

The prefix is stored in clear and shown in the admin UI, so a key can be identified in a log or a
support conversation without revealing it. The secret is shown **once**, at creation, and never
again.

The `pk_` prefix also lets the authentication handler tell a partner key from a Keycloak JWT by
looking at the first three characters, so one `Authorization: Bearer` header serves both without
attempting a JWT parse on every partner request.

### D2 — Hashing: HMAC-SHA256 with a server-side pepper, not a password hash

A password hash (Argon2, bcrypt) is deliberately slow to make guessing a low-entropy human secret
expensive. An API key is 32 bytes of CSPRNG output — about 190 bits. Guessing it is infeasible
regardless of hash speed, so a slow hash buys nothing and costs tens of milliseconds on **every
partner request**, including the p95 the PRD caps at 10 seconds for a whole order.

HMAC-SHA256 with `ApiKeys:Pepper` is the right instrument: fast, constant-time to compare, and the
pepper means a stolen database alone does not permit offline verification of candidate keys. This
is the reading of the PRD's "modern hashing" for keys; passwords are Keycloak's problem and get a
real password hash there.

**Consequence:** rotating the pepper invalidates every issued key. It is a break-glass action, not
a routine one, and the runbook says so.

### D3 — Rotation with overlap, revocation, suspension

`RotateAsync` issues a new key and gives the old one a configurable expiry rather than killing it
instantly, so a partner can deploy the new key before the old stops working. Revocation is
immediate. Suspending a partner rejects every key it holds, without deleting anything.

### D4 — Scopes are a closed set, narrowed at the key

`customers:read`, `customers:write`, `orders:create`, `orders:read`, `invoices:read`. A key's
scopes must be a subset of its partner's. Endpoints are gated by a `partner:{scope}` policy, and a
missing scope is `403 INSUFFICIENT_SCOPE`.

`PartnerPrincipal.TryFrom` **drops any scope string outside the closed set** when building the
principal. An unrecognised scope must never survive to be mistaken for a granted one.

### D5 — The partner id comes only from the key, never from the request

`PartnerPrincipal(PartnerId, ApiKeyId, Scopes)` is built solely from claims the authentication
handler produced. No partner-facing request DTO has a `PartnerId` property — there is nothing to
bind, so no caller can name a partner it is not. An architecture test fails the build if such a
property appears.

Every partner-facing query takes the principal and filters on `partner_id` **in SQL**, not in
application code after loading.

### D6 — A foreign record is 404, not 403

Requesting another partner's order returns **404 Not Found**, never 403 Forbidden. A 403 confirms
the record exists, which lets a partner enumerate identifiers and learn about a competitor's
traffic. 404 reveals nothing. PRD acceptance scenario 4 tests this directly.

### D7 — Per-partner rate limiting, in-process

The ASP.NET rate limiter is partitioned by partner id at `Partner.RateLimitPerMinute`. A rejection
returns the standard error body with `RATE_LIMITED`, `retryable: true` and `Retry-After: 60`.

**It is in-process.** With *N* API replicas a partner's effective limit is *N* × its configured
value — the caveat ADR-0010 raised for the rate limiter, which this does not solve. It is accepted
for the MVP because the limit is a courtesy guard against runaway clients, not a billing boundary
or a security control. Making it exact needs a shared counter (Redis, or a Postgres token bucket),
and that belongs with the decision to run more than one replica.

### D8 — Usage log, written off the request path

Every authenticated partner request records partner, key, timestamp, method, path, status,
correlation id and duration. It is written through a bounded in-memory channel and batch-inserted,
so logging never blocks a request and a burst drops log rows rather than slowing the API. Rows are
pruned after 90 days by a recurring job.

**The key itself never appears in a log**, and a test asserts it by capturing the Serilog sink.

## Consequences

- **Keycloak is not on the partner request path.** A Keycloak outage stops humans logging in; it
  does not stop partner orders.
- **A lost key cannot be recovered, only rotated.** The secret is not stored.
- **The rate limit is approximate at `replicas > 1`** (D7).
- **Pepper rotation is a mass revocation** (D2).
- **Partner authentication is ours to maintain** — the key format, the hash, the scope check. That
  is deliberate: the alternative was modelling every partner as an OIDC client, which is a heavier
  dependency for a narrower need.
