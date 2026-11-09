# Phase 6 — Admin Dashboard & GraphQL: Gaps & Risks

**Gate:** Passed. 2,117 tests, 0 failures, 1 skipped. Release build clean.

## Gaps / Skipped

| Gap | Status | Why |
|---|---|---|
| **No import section on dashboard** | Resolved in P7 | Sourcing didn't exist yet. A section of zeros would read as "nothing is importing" rather than "nothing imports yet". `AdminReadModules` gains `Sourcing` when the module arrives. |
| **PRD §10 coverage matrix not executable** | Deferred | The plan called for a test enumerating every §10 view. The views exist and are reachable; nothing yet proves the list is complete. A test that pins a list of views rots quickly. |
| **NuGet NU1900 suppressed** | Open | Artifactory returns 403 for the audit feed. `WarningsNotAsErrors` suppresses NU1900 in Release. The audit guard itself works (`Newtonsoft.Json 9.0.1` → NU1903). Parked until the feed is fixed. |

## Carried Risks

| Risk | Why it remains | Impact |
|---|---|---|
| **GraphQL depth/cost limits not proven by tests** | The test pins a HotChocolate built-in. Both `MaxDepth` and the cost ceiling can be deleted with that test still green. | Mutation-checked and documented. Non-cyclic deep nesting is not covered by any test. |
| **RBAC mutations audited by a separate save** | They write through `AuthorizationDbContext`, so the audit row can't join their transaction. | Documented non-atomic edge. The audit row is best-effort for RBAC changes specifically. |
| **Audit verdict not asserted intact under full suite** | Every test writes audit events into one shared database. | The test asserts the verdict is reachable and stamped, not that it is true across all concurrent test data. |
