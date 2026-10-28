# ADR-0022: Remove the Iran-Market Modules; Strip Customer to a Shell

## Status
Accepted — 2026-10-28.

Supersedes [ADR-0015](0015-payment-gateway.md) in full (the Payment module it describes no longer
exists; the MVP's payment model is recorded in ADR-0018). Amends
[ADR-0009](0009-supplier-brand-m2m.md) only in that the Supplier/Brand modules it describes were
never present in this repository.

## Context

This repository is a template that was built for an Iranian B2C marketplace. The MVP it is now
being used for — `docs/product-scope/ecommerce_mvp_prd.md`, designed in
`docs/superpowers/specs/2026-10-28-ecommerce-mvp-design.md` — is a Singapore-seller, API-first B2B
platform. Three of the five modules present model a market this product does not serve, and a
fourth models a customer that does not exist here:

- **Payment** is an anti-corruption layer around Iranian bank Internet Payment Gateways, with
  `BankTransaction`, provider selection, failover, callbacks and reversal, and a money boundary
  that accepts only `IRR`. The PRD has **no payment gateway at all** (§2.3): website orders are
  paid outside the system and confirmed by an admin; partner orders are reported paid by the
  partner.
- **Location** is a province/city/district hierarchy for Iranian addresses. The MVP has no such
  requirement; a billing address is a value object on the customer.
- **Notification** sends SMS through Iranian providers. The MVP's only outbound email is Keycloak's
  own account-verification and password-reset mail (PRD §11), and it sends no SMS.
- **Customer** carries `NationalCode`, `CompanyNationalId`, Shahkar identity verification,
  `LegalInformation` documents, `TeamMember` and `JobType`. The MVP's customer is either a website
  account (one Keycloak user) or a partner-linked identity keyed by
  `(partner_id, external_customer_id)`, with optional contact and billing details and a
  field-level contact-change audit trail (spec §5.2).

Leaving this code in place is not free. `Customer.Application` references `Location.Contracts`, so
Location cannot be removed while the old Customer survives. Every module here is wired into the one
shared `EwaysVNextDbContext` and into `Program.cs`, so each dead module keeps a schema, a set of
entity configurations and a slice of host startup alive. The architecture tests enumerate modules by
name, so modules nobody maintains still constrain refactors of the ones that matter.

## Decision

**Delete the Payment, Location and Notification modules outright, and strip Customer to a
compile-only shell that phase 1 rebuilds.**

Removed in this change:

| Removed | Reason |
|---|---|
| `src/Modules/Payment/` (including its `Tests/`) and `tests/PaymentTests/` | PRD §2.3: no payment gateway. Payment/fulfillment status move onto the `Order` aggregate (ADR-0018). |
| `src/Modules/Location/` | No PRD requirement; addresses become a value object in Customer. |
| `src/Modules/Notification/` | Email only, and Keycloak sends it. No SMS in the MVP. |
| Every `.cs` under `src/Modules/Customer/` except `Installer/ModuleInstaller.cs` | Iran-specific identity model; rebuilt in phase 1 per spec §5.2. |
| `src/Database/{Customer,Location}/Configurations/` and the `Customers`, `TeamMembers`, `Provinces`, `Addresses` DbSets | Their entities are gone. |
| `src/Host/.../ModuleAppSetting/{Customer,Notification,Payment}/` | Per-module settings for modules that no longer exist (ADR-0014). |
| `tests/IntegrationTests/CustomerAddresses/`, 19 `tests/UnitTests/*.cs` files | Tests of deleted code. |
| `docs/ADDING-CUSTOMER-PAYMENT-MODULES.md` | A walkthrough of two modules that no longer exist. |

Catalog is **kept and extended** (phase 2). Its `Category`, `AttributeBlueprint`,
`MeasurementUnit`, `CategoryAttributeAssignment` and `CategoryAttributeExclusion` carry real design
work that the MVP's catalog needs.

The Customer module keeps its six projects and a no-op `AddCustomerModule(IServiceCollection,
IConfiguration)`. A shell rather than a deletion, because phase 1 rebuilds the module in the same
place and deleting six projects only to recreate them would churn the solution file, the Host
wiring and the architecture tests for no gain.

`tests/ArchitectureTests/HexagonalBoundaryTests.cs` is introduced here with
`ModuleNames = ["Catalog", "Customer"]` and a test asserting that `src/Modules` contains exactly
those. It is the guard that makes this decision stick: a module added without a `ModuleNames` entry
would escape every boundary rule with the build still green, so the test fails on any directory
that is not named.

## Consequences

- **No data migration, and none is possible.** There is no production database behind this
  template; the SQL Server migration history is replaced wholesale by a fresh Postgres `init`
  (ADR-0017). Anyone with real data in a deployment of the old template cannot upgrade to this
  codebase — it is a different product.
- **`Customer` does not work until phase 1.** The shell registers nothing. The Host starts, Catalog
  works, and no customer endpoint exists. This is deliberate and short-lived.
- **ADR-0015 is superseded.** Its gateway architecture, provider policies and onboarding guide
  describe code that no longer exists. It stays in `docs/adr/` as history.
- **`GraphQLBootstrapTests` now exercises Catalog** instead of Customer. The test's subject is that
  `AddGraphQLBootstrap` is idempotent across modules, not Customer specifically, so it was
  repointed rather than deleted; the coverage is unchanged.
- **One pre-existing unit-test failure is unaffected**:
  `CategoryPersistenceModelTests.UnifyCategoryStatus_Migration_Must_Backfill_Both_Directions_Before_Dropping_Source_Columns`
  asserts on a migration type absent from this template copy. ADR-0017's fresh `init` removes the
  migration set it inspects, and that test is resolved there.
