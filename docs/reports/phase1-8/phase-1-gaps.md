# Phase 1 — Identity & Tenancy: Gaps & Risks

**Gate:** Passed 2026-11-01. 1,067 tests, 0 failures. Release build clean.

## Gaps / Skipped

None. Partners, API keys (peppered, revocable), Keycloak realm imports, RBAC with editable
permission rows, the `PartnerApiKey` authentication scheme, and the admin/partner/shop policy
gates were all delivered.

## Carried Risks

| Risk | Why it remains | Impact |
|---|---|---|
| **Rate limiter is in-process** | ADR-0010. Each replica enforces its own window. | With replicas > 1 the effective partner limit multiplies by the replica count. Must be settled before scaling out. |
| **No automated test obtains a real Keycloak token** | Realms are asserted structurally (content, client config, placeholder secrets). | A misconfigured claim mapping would pass structural tests. The phase 8 smoke script partially closes this. |
| **Usage log entries dropped when buffer full** | The drop is logged and countable; entries lost on unclean shutdown are not recoverable. | Acceptable for MVP — usage log is informational, not billing. |
| **Acceptance 13 partly covered** | FX, currency and refund routes don't exist yet in P1. | Closed by phases 2–5 delivering those routes. |
