# Phase 1 — Identity & Tenancy

**Gate passed 2026-11-01.** 1067 tests, 0 failures. Release build clean: 0 warnings, 0 errors.

Phase 1 answers one question for every request: *who is this, and what may they do?* Humans
authenticate through Keycloak; partners authenticate with keys this system issues and can revoke.

## What works

| Area | What was built |
|---|---|
| Realms | Two production Keycloak exports. Shop allows registration with email verification; Backoffice allows none, requires TOTP at first login, holds passwords to 14 characters and expires an idle session in 15 minutes. Neither client accepts a password grant |
| Permission matrix | Spec §6.1, seeded by migration. Backoffice authority comes from Keycloak realm roles, resolved against editable permission rows |
| Partner keys | `pk_<prefix>_<secret>`, HMAC-SHA256 under a pepper, issue / rotate-with-overlap / revoke / suspend, and an authentication scheme that routes a key away from the JWT reader |
| Scopes & limits | One policy per scope; a per-partner rate limit read from the key's own claim |
| Usage log | Every authenticated partner call, written in batches off the request path, pruned at 90 days |
| Settings | Seller profile, versioned payment instruction, commerce limits — three singletons seeded by migration |
| Customer | Both buyer kinds, contact-change history, shop profile endpoints with lazy creation, backoffice list/detail/suspend |

Everything above is reachable over **both REST and GraphQL**, gated identically.

## Counts

| Suite | Tests | Needs |
|---|---|---|
| ArchitectureTests | 51 | nothing |
| UnitTests | 852 | nothing |
| PersistenceContractTests | 12 | nothing |
| IntegrationTests | 152 | Docker |
| **Total** | **1067** (2 skipped) | |

## Verified on real infrastructure

Against the dev stack on 2026-11-01:

- Five migrations apply in order — `Init`, `Partners`, `PartnerUsage`, `Settings`,
  `CustomerRebuild` — plus `AdminPermissionMatrix` on the authorization context.
- Both Keycloak realms import and serve `.well-known/openid-configuration`.
- The host serves `/health` 200 and GraphQL answers; `/api/v1/admin/partners` and
  `/api/v1/shop/me` both refuse an unauthenticated caller with 401.
- Partial unique indexes, check constraints and append-only grants all reject bad rows.

## What this phase found

Six faults, each now covered by a test. Three are worth reading about because the same mistake is
easy to repeat.

1. **A type-level `[Authorize]` on a GraphQL schema extension gates the whole merged type.** The
   admin surface's `admin:super` policy was judging a shop customer's `me` call, and the error
   named the shop code rather than the admin extension that caused it. Found by adding a resolver
   with no attribute at all and watching it be refused. `GraphQLGateScopeTests` now forbids
   type-level gates and ungated resolvers alike.
2. **The two permission enforcement paths had drifted.** REST forwarded Backoffice realm roles;
   the GraphQL field attribute did not, so every admin GraphQL field refused every caller once
   authority moved to Keycloak. Both now call one `BackofficeRoles.From`.
3. **A module's endpoints are never mapped unless the module registers them.** The Host scans only
   its own assembly, so the partner admin routes 404'd with an empty body rather than failing at
   startup.
4. `UseRateLimiter` ran before `UseAuthentication`, so `context.User` was always anonymous and the
   per-partner limit could never have applied.
5. Reading configuration eagerly at registration is not the same as per request: under
   `WebApplicationFactory` the test's configuration arrives later, so an existing rate-limit test
   stopped limiting while still passing.
6. Seeding broke EF InMemory twice — once by making `RowVersion` required where it is not
   store-generated (54 unrelated failures), once by seeding a `string[]` into a `List<string>`
   backing field.

## Risks carried forward

- **The rate limiter is in-process.** With more than one replica each instance enforces its own
  share, so a partner limited to 300/min could achieve 300 × replicas. [ADR-0010](../adr/0010-kubernetes-deployment-strategy.md)
  must be settled before `replicas > 1`.
- **Authentication costs one indexed lookup per partner request, with no cache.** That is the
  deliberate trade: revocation and suspension take effect on the very next request. If it becomes
  a bottleneck, any cache added must bound staleness explicitly.
- **Usage entries are dropped when the buffer is full**, and lost entirely on an unclean
  shutdown. The drop is logged and countable; the loss is not recoverable.
- **No test authenticates a real human.** The realms are asserted structurally — content, client
  configuration, placeholder secrets — and imported successfully by hand, but no automated test
  obtains a token from Keycloak.
- **Acceptance 13 is partly covered.** Partners, keys, settings and the RBAC GraphQL are tested
  over HTTP; the FX, currency and refund rows have no routes yet and rest on the matrix unit test
  until phases 2–3 and 4–5.

## Reproducing the gate

```bash
docker compose -f build/compose/docker-compose.dev.yml up -d

dotnet build EwaysVNext.slnx -c Release
dotnet test EwaysVNext.slnx

dotnet run --project src/Host/EwaysVNext.Host -- --migrate
```

The dev Postgres binds `POSTGRES_PORT` (default 5432); set it if the host already runs one.
