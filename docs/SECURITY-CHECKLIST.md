# Security checklist

What this system does to protect itself, where each thing is enforced, and what is **not**
covered. The last column is the point: a checklist that only lists wins is a checklist nobody
can act on.

---

## Before go-live

- [ ] Every value in `build/compose/.env` is set and generated for this deployment. Compose uses
      `${VAR:?}` for every genuine secret, so a missing one fails the stack rather than
      defaulting — a default password that reaches production is worse than an outage.
- [ ] `ApiKeys__Pepper` is unique to this deployment and backed up. **Rotating it invalidates
      every partner key ever issued**, which is a recovery tool and an outage in the same lever.
- [ ] `Sourcing:Crawler:AllowPrivateNetworks` is `false`. It is false by default; confirm
      nothing has overridden it. Only the integration suite sets it true.
- [ ] `OpenApi:Expose` is deliberate. True publishes the partner contract — usually right, since
      partners generate clients from it. Scalar, the browsable UI, is Development-only and
      should not be reachable.
- [ ] The database roles exist and `eways_app` owns nothing. The smoke script checks this by
      trying to create a table as that role and expecting permission denied.
- [ ] Exactly one worker. More is safe for the job store, which uses `SKIP LOCKED`, but the rate
      limiter and candidate cache are per process (ADR-0010), so a second doubles effective
      limits.
- [ ] TLS terminates at Caddy with a real certificate, and nothing but Caddy is published. The
      smoke overlay binds service ports to `127.0.0.1` for local runs only.

---

## What is enforced, and where

### Authentication and authorization

| Protection | Where | Not covered |
|---|---|---|
| Humans authenticate through Keycloak; partners present an API key | `AuthSchemes.IsPartnerKey` routes `Bearer pk_…` before any JWT code sees it | Keycloak's own hardening — password policy, brute-force, session lifetime — is configured there, not here |
| Admin surfaces are gated twice: realm policy **and** permission | `.RequireAuthorization(...)` + `.RequirePermission(...)` on every admin route | A permission row is editable data; the realm policy is what bounds a misedited one |
| Backoffice authority comes from realm roles, and only when the tenant claim is `Backoffice` | `BackofficeRoles.From(user)` | The Shop realm is open self-service registration — a shop token claiming `super_admin` is refused, and there is a test for exactly that |
| Every route is authorized unless explicitly allowlisted | `EndpointAuthorizationTests`, over the live routing table | The allowlist is argued in that file; a new anonymous route has to be added there consciously |
| Refunds need super admin **and** the service re-checks | Route policy, then `IsSuperAdmin` in `OrderAdminService` before the order is loaded | Nothing stops a super admin refunding; it is audited, not prevented |

### Tenancy and data separation

| Protection | Where | Not covered |
|---|---|---|
| A partner's id is never bindable; it arrives as an authenticated principal | `PartnerTenancyArchitectureTests` fails the build on any request shape carrying a `PartnerId` | — |
| Partner-scoped reads narrow in SQL, not after loading | `PartnerOrderQueries.Scoped` | A query written outside that helper would not be narrowed; the convention is enforced by review |
| A customer's orders are filtered by customer **and** channel | `ShopOrderQueries.Mine` | — |
| Another customer's order is 404, not 403 | Shop and partner read sides | 403 would confirm the order exists, which is not the asker's business |

### Input and request safety

| Protection | Where | Not covered |
|---|---|---|
| **No shop request may carry a price.** Only `expectedTotal`, which can cause a refusal and never a price | `Shop_Request_Dtos_Accept_No_Money_Except_ExpectedTotal`, mutation-checked | — |
| The crawler refuses private addresses, checked on every **resolved** IP | `SafeHttpHandler`, mutation-checked on `169.254.169.254` | **No integration test against a real HTTP server.** That the handler is on the client and redirects are off is verified by reading the registration |
| Redirects are disabled on the crawl client | `ConfigurePrimaryHttpMessageHandler` | A redirect to a private address would otherwise walk around the guard |
| GraphQL depth and cost are bounded | `GraphQLLimits` plus HotChocolate's built-in cycle guard | **The test that demonstrates refusal is passing on the framework's default**, not on the configured limits — both can be deleted with it still green. Non-cyclic deep nesting is untested |
| Rate limiting per partner key, per admin subject, per anonymous IP | `RateLimitingSetup` | In process. With replicas > 1 the effective limit multiplies by the replica count |
| Uploaded import files are size-capped and parsed as text | `Sourcing:Files:MaxBytes`, the readers | XLSX is refused rather than parsed, so no spreadsheet-formula risk exists today |

### Money and records

| Protection | Where | Not covered |
|---|---|---|
| Money is integer minor units end to end; never a `double` | `MinorUnits`, `PriceParser`, the composition engine | — |
| Checkout refuses a price that moved, writing nothing | `CheckoutService`, mutation-checked | — |
| Issued documents are never edited; a refund adds a linked credit note | `Order.RecordRefund`, asserted byte-for-byte before and after | — |
| Every admin write is audited in the same save as the change | `AdminAuditCoverageTests` (structural), each service's own tests (ordering) | RBAC mutations write through a separate context, so their audit row is a second save — a documented non-atomic edge |
| The audit chain is hash-linked and verifiable | `GET /api/v1/admin/audit/verify`, super admin only | Verification proves the chain is self-consistent; it cannot prove rows were never added by somebody with database access |

### Rights and third-party material

| Protection | Where | Not covered |
|---|---|---|
| Crawl, content use and image use are three separate permissions | `SourcePolicy` | — |
| A stored policy overrules a file attestation | `RightsDecision.For` | The attestation is whoever uploaded a spreadsheet; the policy is the seller's own decision |
| Denied content is an error nobody can approve away | `RecordValidator` | Unknown rights are a review item — somebody takes responsibility, and `RightsApprovedBy` records who |
| robots.txt is honoured, with an honest user agent | `RobotsTxt`, `CrawlerOptions` | Politeness is per worker, so two workers each honour the delay separately |

---

## Known gaps

Stated plainly, because each is something an attacker or an auditor would find.

1. **The crawler's assembly is untested.** Its decision logic is thoroughly unit-tested; that
   the safe handler is actually installed is not. A refactor could disconnect it and every test
   would stay green.
2. **The GraphQL limits are not demonstrated to work.** See the table above. The protection
   observed in testing is HotChocolate's own.
3. **Rate limits and the candidate cache are in process.** Horizontal scaling multiplies them.
4. **Brave search has never run against the live service.** Disabled without a key, so exposure
   is limited to deployments that configure one.
5. **No secret scanning or dependency audit in CI.** Secrets are kept out of the repository by
   convention and `.env` files; nothing automated enforces it.
6. **No penetration test.** Nothing here has been examined by somebody trying to break it.

---

## Reporting

A vulnerability in this system should go to the seller's own security contact before anywhere
else. Nothing in this repository publishes an address, deliberately: it is the operator's to
choose.
