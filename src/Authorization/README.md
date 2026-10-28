# Authorization

A cross-cutting RBAC subsystem — permissions, roles, scopes, and per-user role/scope
assignments — used to gate both REST and GraphQL requests across the whole Host. It lives
at `src/Authorization/`, a sibling of `src/BuildingBlocks/` and `src/Modules/`, **not**
under `src/Modules/` itself.

`Role` and `Scope` (renamed from `ProductScope`) are real database-backed tables, not
enums — like `Permission`, they're generic, admin-manageable reference data with full CRUD,
not a fixed compile-time set. Adding a new role or scope is a data change (the `createRole` /
`createScope` mutations), not a code change and redeploy.

Its whole management surface is **GraphQL**, on the Host's single `/graphql` endpoint — there
are no REST routes. Writes (create/update/delete/assign/remove/bootstrap) are mutations, reads
(list/get) are queries. See [GraphQL surface](#graphql-surface) below.

## Why it isn't a module

Every module in `src/Modules/` owns a business capability (Catalog, Customer, …) and
follows the `Application/Adapter/QueryApplication/Installer` hexagonal layout on the
shared `EwaysVNextDbContext`. Authorization doesn't fit that shape on purpose:

- It's a capability every module *consumes*, not a bounded context any module owns.
  Forcing it into the module system would mean inventing new cross-module ACL
  exceptions (like the existing `Orders → Catalog` / `Payment → Customer` write-side
  carve-outs) just so other modules could reach it.
- It doesn't fit `src/BuildingBlocks/` either — BuildingBlocks stay generic with no
  concrete entities of their own; `Permission`, `PermissionRule`, `Role`, `Scope`, and
  `UserRoleScope` are real business data with their own persistence.
- It's deliberately decoupled from the rest of the app: it has no `Program.cs` of its
  own (Host owns process startup), and it doesn't reference `BuildingBlocks/Core`
  (`Actor` / `IUserProfileService`) — it reads the caller's identity directly off JWT
  claims (`ClaimTypes.NameIdentifier` / `"sub"`).

It's a fourth category, parallel to `BuildingBlocks/` and `Modules/` inside `src/` —
closest in spirit to a small internal IAM service that happens to run in-process.

## Layout

```
src/Authorization/
├── Authorization.Abstractions/    ← what OTHER modules reference. Zero business-logic deps.
│                                     PermissionRequirement (IAuthorizationRequirement),
│                                     DynamicAuthorizationPolicyProvider, AuthorizationPolicyNames
│                                     (the "authz-permission:{code}" policy-name convention),
│                                     .RequirePermission() sugar for IEndpointConventionBuilder
│                                     (REST), and the custom [RequirePermission("code")] GraphQL
│                                     field attribute. Also holds IAuthorizationCheckService
│                                     itself — it's a pure contract, so it belongs here, not in
│                                     Application, since the policy machinery and the GraphQL
│                                     attribute both need it.
├── Authorization.Application/     ← entities (Permission, PermissionRule, Role, Scope,
│                                     UserRoleScope) and ports (I*Service interfaces) for the
│                                     management surface, plus Request/Response DTOs per feature
│                                     area (Permissions/, Roles/, Scopes/, UserRoleScopes/). No
│                                     concrete classes — mirrors BuildingBlocks/Core's
│                                     Application half.
├── Authorization.Implementation/  ← concrete services, AuthorizationDbContext + EF configs +
│                                     migrations, seeding, and PermissionAuthorizationHandler —
│                                     mirrors BuildingBlocks/Core's Implementation half.
└── Authorization.Endpoints/       ← the presentation layer, GraphQL only: HotChocolate mutations
                                      for every write, query types for every read. Also holds
                                      the single AddAuthorizationSystem(...) composition call for
                                      Host, and AddAuthorizationGraphQL() for the schema alone.
```

**There is no role-based authorization *check* anywhere in this subsystem** — every gate, on
both REST and GraphQL, is a permission code. `Role` is real domain data now (its own table,
full CRUD), and `PermissionRule.RoleId`/`UserRoleScope.RoleId` still let a permission rule
require "at least this role's rank" as one of its criteria — but there is no standalone "does
the caller have role X" authorization policy anywhere, on either protocol. `Role.Rank` (an
`int`) preserves the old enum's ordinal semantics: a rule requiring a given role rank is satisfied
by any assigned role with rank ≥ 0, not just an exact match.

Dependency direction: `Abstractions ← Application ← Implementation ← Endpoints`. Endpoint
route handlers only ever resolve `Authorization.Application` interfaces (`IPermissionService`,
`IRoleService`, `IScopeService`, …) via DI — never `Implementation` types directly. Only the
composition method in `Authorization.Endpoints/DependencyInjection.cs` touches
`Implementation`.

## Persistence

`AuthorizationDbContext` uses the exact same `ConnectionStrings:Default` as
`EwaysVNextDbContext` — **one physical database**, not a separate one — but stays its own
`DbContext`:

- Its own SQL schema: `authorization` (vs. the unified context's per-module schemas).
- Its own migrations history table: `authorization.__AuthorizationMigrationsHistory` (vs.
  `dbo.__EFMigrationsHistory`) — schema-qualified explicitly in both `DependencyInjection.cs`
  and `AuthorizationDbContextDesignTimeFactory.cs` (`sql.MigrationsHistoryTable(name, schema)`),
  so the two contexts' migrations can never collide.
- Every environment (Development, Stage, Production) requires a real
  `ConnectionStrings:Default` — there is no InMemory fallback here, unlike the unified
  database's dev convenience.

Because it's a second `DbContext` against the same database, it needs its **own** migration
commands — these are separate from the ones in the root [README](../../README.md#database-migration-helpers):

```bash
# Add a migration after changing anything under Authorization.Application/Entities or
# Authorization.Implementation/Persistence/Configurations
dotnet ef migrations add {Name} \
  -p src/Authorization/Authorization.Implementation/Authorization.Implementation.csproj \
  -s src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj \
  -c AuthorizationDbContext \
  -o Persistence/Migrations

# Apply it directly (independent of the unified EwaysVNextDbContext migration)
dotnet ef database update \
  -p src/Authorization/Authorization.Implementation/Authorization.Implementation.csproj \
  -s src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj \
  -c AuthorizationDbContext
```

`dotnet run --project src/Host/EwaysVNext.Host -- --migrate` also migrates
`AuthorizationDbContext` (and seeds `UserRoleScope` rows from `AuthorizationSeedData`, a
hardcoded const-data class — not configuration) as part of the same explicit `--migrate` step
used for the unified database — the Host still never auto-migrates on startup.

`Role` and `Scope` are reference data, so their default rows ship in the `InitialCreate`
migration itself (EF Core `HasData`, deterministic GUIDs), not through `AuthorizationSeedData`:
`Role` seeds `User` (rank 1), `Manager` (rank 2), `Admin` (rank 3); `Scope` seeds
`Sell`/`Finance`/`Technology`. These are just starting defaults — add more via
`POST /roles` / `POST /scopes` once bootstrapped. `AuthorizationSeedData.UserRoleScopes`
references them by `RoleCode`/`ScopeCode` string (resolved to the actual row's `Id` at seed
time by `DatabaseSeeder`), not by a hardcoded `Guid`, so it stays readable.

## Error handling — `Result`/`Result<T>`, same as every module

Every `I*Service` method (`IPermissionService`, `IRoleService`, `IScopeService`,
`IUserRoleScopeService`) returns `EwaysVNext.Outcomes.Result` or `Result<T>` — never throws for
a refusal it can express, matching the rest of the codebase (AGENTS.md rule 9). There used to be
Authorization-local `NotFoundException`/`ConflictException`/`ValidationException`/
`InvalidPermissionRuleException` types plus a REST `ExceptionTranslationFilter` that caught them;
all of that is gone. A service method now returns `Result.Failure(new Error("CODE", "message"))`
directly for an anticipated refusal (not found, code already taken, still referenced, unknown
role/scope id, …), and `Error`'s code suffix drives the HTTP status (`*_NOT_FOUND` → 404,
`*_TAKEN` → 409, `VALIDATION` → 400 by default) unless the 3-arg `Error` constructor passes an
explicit status (used for `*_IN_USE` and similar 409s that don't match a suffix).

The one domain-level exception that's still thrown internally is
`InvalidPermissionRuleException` (`Authorization.Application/Entities/`), raised by
`Permission.Create`/`Permission.Update` when a rule has neither a role nor a scope — this is a
genuine aggregate invariant, not a service-level check, so it stays a thrown exception at the
domain layer. `PermissionService` is the only place that catches it, immediately at the call
site, and converts it to `Result.Failure(new Error("VALIDATION", ex.Message))` — mirroring
exactly how Catalog's `ProductService` wraps calls into `Product` with
`try { ... } catch (DomainException dex) { return Result.Failure(new Error(dex.Code, dex.Message)); }`,
just with Authorization's own exception type instead of `EwaysVNext.Domain.Common.DomainException`
(Authorization deliberately doesn't reference `EwaysVNext.Domain` — see "Why it isn't a module").

No resolver carries its own validation code (Code/Name-required checks live in the service).
Resolvers call `.ValueOrThrow()` (throws the right typed `*GraphQLException` via
`Error.ToGraphQLException()` on failure) or `.ValueOrDefault()` for the "not found → null"
queries (`permission(id)`, `permissionByCode(code)`), from
`EwaysVNext.Presentation.Endpoint.GraphQL.Extensions` — same helpers `Customer`'s GraphQL
mutations already use.

## How enforcement works — permission checks only, one mechanism per protocol

Every gate is a **permission code** (e.g. `"catalog.manage"`, `"partners.manage"`) resolved
against `IAuthorizationCheckService`, which checks whether the caller satisfies one of the target
`Permission`'s rules — either through a stored `UserRoleScope`, or through a role the token
itself asserts. There is no custom middleware — REST and GraphQL each plug a
permission check into whichever native extensibility point that protocol already has.

**REST** — `DynamicAuthorizationPolicyProvider` (registered as the app's
`IAuthorizationPolicyProvider`) resolves the `"authz-permission:{code}"` policy-name prefix
into an `AuthorizationPolicy` requiring `PermissionRequirement(code)`; anything else delegates
to the app's normal `DefaultAuthorizationPolicyProvider`, so the existing static
`"Admin"`/`"Customer"` policies in `Program.cs` (unrelated Keycloak-claim checks used by other
modules) keep resolving exactly as before. `PermissionAuthorizationHandler` (a plain
`AuthorizationHandler<PermissionRequirement>`) resolves the caller's id from the
`ClaimsPrincipal` (`ClaimTypes.NameIdentifier` / `"sub"`) and calls `IAuthorizationCheckService`.
### Two gates, on purpose

An admin surface is gated **twice**: the group gets `.RequireAuthorization(AuthPolicies.AdminStaff)`
(or `AdminSuper`), and each endpoint gets `.RequirePermission(AdminPermissions.X)`. GraphQL uses
`[Authorize(Policy = ...)]` plus `[RequirePermission(...)]`.

That is not redundancy. Permission rows are **editable data** — a misedited rule could hand staff
something only a super admin should have. The realm policy is not editable from inside the
application, so it bounds the damage a bad row can do. The permission answers "may this user do
this particular thing"; the policy answers "is this caller even in the right realm".

### Where backoffice roles come from

Keycloak, not this database. `PermissionAuthorizationHandler` forwards the token's
`ClaimTypes.Role` values as `CheckAuthorizationRequest.TokenRoleCodes` — **but only when the
`tenant` claim is `Backoffice`**. The Shop realm is open self-service registration, so a role
named there means nothing here however it is spelled; forwarding it would let anyone who could
influence their own Shop token claim backoffice authority. `AdminPermissionMatrixTests` holds
that case.

Token roles count as assignments with no scope, so they satisfy only rules that carry no scope
rows. A scoped rule still needs a real `UserRoleScope` naming the scope.

### The seeded matrix (spec §6.1)

`AdminPermissionMatrix.Rows` is the matrix, seeded via `HasData` with literal GUIDs generated
once and committed — computing them from the code string would make the seed depend on an
algorithm, and changing it later would orphan every row already in a production database.

| Permission | Lowest role |
|---|---|
| `catalog.manage`, `imports.manage`, `customers.manage`, `orders.manage`, `payments.confirm` | `staff` |
| `partners.manage`, `fx.manage`, `currencies.manage`, `settings.manage`, `refunds.manage`, `admin-accounts.manage` | `super_admin` |

`super_admin` (rank 20) outranks `staff` (rank 10) and the rule check is
`assigned.Rank >= rule.Rank`, so a super admin satisfies every staff rule without a second row.
Both ranks sit well above the legacy `User`/`Manager`/`Admin` ladder so neither can reach a
backoffice rule by outranking it.

Reference only `Authorization.Abstractions` and use the sugar, which just calls the standard
`.RequireAuthorization(policyName)` under the hood:

```csharp
using AuthorizationService.Abstractions.Extensions;

group.MapPost("/reports", CreateReport)
    .RequireAuthorization()
    .RequirePermission("reports.create");
```

Like any ASP.NET Core authorization failure, the response is **401** if the caller isn't
authenticated at all, and **403** if they're authenticated but the check fails — standard
framework behavior (`AuthorizationMiddleware`/`PolicyEvaluator` choosing Challenge vs. Forbid
based on whether authentication itself succeeded), not something this subsystem implements.

**GraphQL** — there's no clean way to reference a helper method from inside
`[Authorize(Policy = "...")]` (attribute arguments must be compile-time constants), and the
ASP.NET Core policy indirection isn't needed here anyway, so GraphQL gets its own real
attribute instead: `RequirePermissionAttribute` (in `Authorization.Abstractions.Attributes`)
is a HotChocolate `ObjectFieldDescriptorAttribute` — the same extensibility point HotChocolate's
own `[UsePaging]`/`[UseFiltering]` are built on. `OnConfigure` attaches a field middleware via
`descriptor.Use(...)` that resolves `IHttpContextAccessor` + `IAuthorizationCheckService` from
`ctx.Services`, checks the permission, and throws a `GraphQLException` (`AUTH_NOT_AUTHENTICATED`
/ `AUTH_NOT_AUTHORIZED`) on failure:

```csharp
using AuthorizationService.Abstractions.Attributes;

[ExtendObjectType(typeof(Query))]
public sealed class ReportQueries
{
    [RequirePermission("reports.read")]
    public IQueryable<Report> Reports([Service] IReportRepository repo) => repo.QueryAll();
}
```

Just the bare code, exactly like `.RequirePermission()` on REST — no policy-name prefix, no
`[Authorize]`, no separate wiring. As of the current state, no GraphQL field actually carries
this attribute (temporarily removed pending re-application — see the GraphQL surface table
below); the attribute itself is unchanged and ready to reapply.

## GraphQL surface

Everything is under the Host's single `/graphql` endpoint.

### Mutations

Each mutation takes one `…Input` and returns a `…Payload`: create/update/assign/bootstrap
payloads carry the resulting DTO (`permission`, `role`, `scope`, `assignment`); delete/remove
payloads carry `success`.

| Mutation | Policy | Permission | Notes |
|----------|--------|------------|-------|
| `createPermission(input)` | `admin:super` | `admin-accounts.manage` | Takes a `rules` list (`roleId` and/or `scopeIds` combinations) |
| `updatePermission(input)` | `admin:super` | `admin-accounts.manage` | |
| `deletePermission(input)` | `admin:super` | `admin-accounts.manage` | |
| `createRole(input)` | `admin:super` | `admin-accounts.manage` | `code`, `name`, `rank` |
| `updateRole(input)` | `admin:super` | `admin-accounts.manage` | |
| `deleteRole(input)` | `admin:super` | `admin-accounts.manage` | Refuses (`ROLE_IN_USE`) if still referenced by a `PermissionRule` or `UserRoleScope` |
| `createScope(input)` | `admin:super` | `admin-accounts.manage` | `code`, `name` |
| `updateScope(input)` | `admin:super` | `admin-accounts.manage` | |
| `deleteScope(input)` | `admin:super` | `admin-accounts.manage` | Refuses (`SCOPE_IN_USE`) if still referenced by a `PermissionRuleScope` or `UserRoleScope` |
| `assignUserRoleScope(input)` | `admin:super` | `admin-accounts.manage` | Assign a role in a scope to a user |
| `removeUserRoleScope(input)` | `admin:super` | `admin-accounts.manage` | |
| `bootstrapAdmin(input)` | `admin:super` | — | **Vestigial.** It assigns the highest-`Rank` role to the caller, which is now `super_admin` — so before it was gated, the first authenticated caller to reach it became a super admin. Backoffice authority comes from Keycloak realm roles now, not from a stored assignment, so nothing needs it |

Every resolver class carries `[Authorize(Policy = AuthPolicies.AdminSuper)]`, so an anonymous
caller gets `AUTH_NOT_AUTHENTICATED` (HTTP 401) and a non-super-admin gets `AUTH_NOT_AUTHORIZED`
(HTTP 403), both before any service runs. `GraphQLHttpResponseFormatter` maps the two codes to
those statuses; a refusal the service returns surfaces with the service's own code via
`.ValueOrThrow()`.

**Why the class, not the method.** `RbacGraphQlGatingTests` reflects over every resolver and
fails the build if one is missing its policy. The gate used to be a bare `[Authorize]` — any
authenticated caller, including a website customer — on a surface that edits the permission
matrix itself, which made it self-escalating: `createPermission` could grant its own caller
`partners.manage`.

### Queries

| Field | Policy | Notes |
|-------|--------|-------|
| `permissions` | `admin:staff` | List all |
| `permission(id)` | `admin:staff` | Returns `null` on not-found (`.ValueOrDefault()`) |
| `permissionByCode(code)` | `admin:staff` | Same |
| `roles` | `admin:staff` | All `Role` rows, ordered by `Rank` descending |
| `scopes` | `admin:staff` | All `Scope` rows |
| `userRoleScopesForUser(userId)` | `admin:staff` | |

Reads need at least `staff`: who may do what is not public information, and it maps the admin
surface for anyone probing it. These fields previously carried no attribute at all and answered
anonymous callers. `checkAuthorization(input)` remains removed.

## Wiring it into a host

One call does everything — DbContext registration, the application services, the
policy provider + handlers, and the GraphQL query + mutation type registrations
(`AddAuthorizationGraphQL()`, also callable on its own to build just the schema):

```csharp
builder.Services.AddAuthorizationSystem(builder.Configuration);
...
app.UseAuthentication();
app.UseAuthorization();   // ASP.NET Core's own middleware now does all the enforcement
...
app.MapGraphQLEndpoint(); // already called once by Host; Authorization's queries and mutations ride the same schema
```

## Known gaps

- `tests/ArchitectureTests` currently has two checks (`DeterministicTimeTests`,
  `EndpointResultUsageTests`), and both hard-code narrow scan roots
  (`src/Modules/*/Application/Domain`, `src/Modules/*/Adapter/Inbound/Rest`) that don't match
  this subsystem's own folder layout — so moving it under `src/` did **not** bring it into
  scope, and nothing automated currently guards its conventions the way it does for a module.
- No admin UI or other caller currently exercises `PermissionService`/`UserRoleScopeService`
  beyond its own GraphQL mutations and the seeded bootstrap flow.
- **The bootstrap deadlock is closed.** It used to be circular: a permission gate could only be
  satisfied by a row that only the gated mutation could create, so nothing could call it. Two
  things resolved that. The §6.1 matrix now ships as seed data in the `AdminPermissionMatrix`
  migration, so the rows exist before anyone connects; and backoffice authority comes from a
  Keycloak realm role rather than a stored assignment, so the first super admin is created in
  Keycloak, outside this database entirely. `bootstrapAdmin` is vestigial as a result — see the
  GraphQL surface above for why it is nonetheless gated.
