# Centralized GraphQL Composition Design

## Goal

Centralize the Hot Chocolate server and endpoint configuration while keeping every GraphQL type physically and logically owned by its module.

## Current State

Catalog currently creates a named `CategoryAdmin` server, defines the shared query and mutation roots, applies global GraphQL configuration, and maps `/api/v1/admin/category/graphql`. The Host also contains Catalog-specific endpoint and Nitro wiring. Admin's GraphQL adapter is currently a no-op even though ADR-0007 assigns the shared admin GraphQL API to Admin.

## Decision

Admin's inbound GraphQL adapter owns the single server/schema composition infrastructure:

- call `AddGraphQLServer()` exactly once;
- create the shared `Query` and `Mutation` roots;
- register authorization and global scalar/options configuration;
- map the canonical `/api/v1/admin/graphql` endpoint;
- map and configure Nitro centrally.

Each GraphQL-capable module exposes an explicit, compile-time-safe contribution method over `IRequestExecutorBuilder`. Catalog's contribution registers its query and mutation type extensions but does not create a server or map an endpoint. Modules without GraphQL require no GraphQL registration code.

The Host installs the modules, obtains the central Admin GraphQL builder, applies the public module contribution methods, and invokes the central Admin endpoint mapping. It knows contribution APIs but does not register or reference concrete resolver types.

No reflection, assembly scanning, marker interface, or runtime registration registry will be introduced.

## Ownership and Dependency Direction

```text
Catalog.Adapter.Inbound.GraphQL
  owns CategoryAdminQueries and CategoryAdminMutations
              |
Catalog.Installer
  exposes AddCatalogGraphQlTypes(IRequestExecutorBuilder)
              |
Host
  composes enabled module contributions
              |
Admin.Adapter.Inbound.GraphQL
  owns the one server and canonical endpoint
```

Catalog resolver implementations remain inside Catalog. Admin does not reference Catalog resolver types or the Catalog GraphQL adapter. Host references the public contribution API exposed through the Catalog Installer, not concrete resolver classes.

## Endpoint and Compatibility

The only GraphQL endpoint is `/api/v1/admin/graphql`. The old `/api/v1/admin/category/graphql` endpoint and its Nitro path are removed. This is an approved API change; affected tests and consumers are updated rather than retaining an alias.

The endpoint keeps the existing `Admin` authorization policy and `admin` rate-limit policy. Field-level Admin authorization remains unchanged.

## Hot Chocolate Registration

Global registration in Admin includes the existing `long` to `ID` binding so Snowflake IDs continue to serialize safely as strings. Catalog's contribution uses `AddTypeExtension<CategoryAdminQueries>()` and `AddTypeExtension<CategoryAdminMutations>()` against the shared request-executor builder.

The existing global behavior is preserved unless centralization requires relocation. No new GraphQL types, operations, DTOs, business behavior, or module abstractions are introduced.

## Expected Changes

- Admin GraphQL dependency registration: implement central server and endpoint/Nitro mapping.
- Admin Installer: expose or invoke the central GraphQL composition API consistently with existing installer conventions.
- Catalog GraphQL dependency registration: replace server and endpoint ownership with type contribution only.
- Catalog Installer: expose the public Catalog GraphQL contribution method without exposing concrete resolver types to Host.
- Host startup: compose the central server with Catalog's contribution and map only the canonical endpoint.
- Project references: remove direct Host dependencies that are no longer required and add only references needed for the public composition APIs.
- Tests: update executor/route setup and add architectural assertions for centralized registration and module ownership.

## Verification

- Source scan finds exactly one `AddGraphQLServer()` invocation.
- No module other than Admin maps a GraphQL or Nitro endpoint.
- Catalog resolver files remain under Catalog's inbound GraphQL adapter.
- Host contains no reference to `CategoryAdminQueries` or `CategoryAdminMutations`.
- The executable schema contains the existing Category queries and mutations.
- `/api/v1/admin/graphql` is authorized and executable; the old Category endpoint is absent.
- Existing GraphQL tests, affected unit/architecture tests, affected project builds, and `git diff --check` pass.
