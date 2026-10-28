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

Every gate is a **permission code** (e.g. `"notification.send"`, `"authorization.permissions.read"`)
resolved against `IAuthorizationCheckService`, which checks whether the caller has a
`UserRoleScope` satisfying one of the target `Permission`'s rules. There is no role-based
authorization policy anywhere, and no custom middleware — REST and GraphQL each plug a
permission check into whichever native extensibility point that protocol already has.

**REST** — `DynamicAuthorizationPolicyProvider` (registered as the app's
`IAuthorizationPolicyProvider`) resolves the `"authz-permission:{code}"` policy-name prefix
into an `AuthorizationPolicy` requiring `PermissionRequirement(code)`; anything else delegates
to the app's normal `DefaultAuthorizationPolicyProvider`, so the existing static
`"Admin"`/`"Customer"` policies in `Program.cs` (unrelated Keycloak-claim checks used by other
modules) keep resolving exactly as before. `PermissionAuthorizationHandler` (a plain
`AuthorizationHandler<PermissionRequirement>`) resolves the caller's id from the
`ClaimsPrincipal` (`ClaimTypes.NameIdentifier` / `"sub"`) and calls `IAuthorizationCheckService`.
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

| Mutation | Permission | Notes |
|----------|------------|-------|
| `createPermission(input)` | *(none — just authenticated)* | Takes a `rules` list (`roleId` and/or `scopeIds` combinations) — permission gate temporarily removed, pending re-application (see note below) |
| `updatePermission(input)` | *(none — just authenticated)* | |
| `deletePermission(input)` | *(none — just authenticated)* | |
| `createRole(input)` | *(none — just authenticated)* | `code`, `name`, `rank` — permission gate temporarily removed, pending re-application (see note below) |
| `updateRole(input)` | *(none — just authenticated)* | |
| `deleteRole(input)` | *(none — just authenticated)* | Refuses (`ROLE_IN_USE`) if still referenced by a `PermissionRule` or `UserRoleScope` |
| `createScope(input)` | *(none — just authenticated)* | `code`, `name` — permission gate temporarily removed, pending re-application |
| `updateScope(input)` | *(none — just authenticated)* | |
| `deleteScope(input)` | *(none — just authenticated)* | Refuses (`SCOPE_IN_USE`) if still referenced by a `PermissionRuleScope` or `UserRoleScope` |
| `assignUserRoleScope(input)` | *(none — just authenticated)* | Assign a role in a scope to a user — permission gate temporarily removed, pending re-application |
| `removeUserRoleScope(input)` | *(none — just authenticated)* | |
| `bootstrapAdmin(input)` | *(none — just authenticated)* | Assigns the highest-`Rank` role to the caller (user id from the token's `NameIdentifier`/`sub` claim) in `input.scopeId`; refuses (`ADMIN_ALREADY_EXISTS`) once someone already holds that role. Can't require a permission here — nothing could be granted yet |

Every mutation carries HotChocolate's `[Authorize]`, so an anonymous caller gets
`AUTH_NOT_AUTHENTICATED` (HTTP 401) before any service runs. No mutation carries
`[RequirePermission]` right now (see the note below); once one does, a caller without the
permission gets `AUTH_NOT_AUTHORIZED` (HTTP 403). `GraphQLHttpResponseFormatter` maps both codes
to those statuses. A refusal the
service returns surfaces as a GraphQL error with the service's own code, via `.ValueOrThrow()`.

**Temporary state, not a design decision:** `authorization.permissions.manage`,
`authorization.roles.manage`, `authorization.scopes.manage`, and
`authorization.user-role-scopes.manage` are all removed from their mutations for now, so no
mutation is permission-gated — these are slated to come back; don't take the current (empty) gate
list as final.

### Queries

| Field | Permission | Notes |
|-------|------------|-------|
| `permissions` | *(none — not even authentication, see below)* | List all |
| `permission(id)` | *(none — not even authentication)* | Returns `null` on not-found (`.ValueOrDefault()`) |
| `permissionByCode(code)` | *(none — not even authentication)* | Same |
| `roles` | *(none — not even authentication)* | All `Role` rows, ordered by `Rank` descending |
| `scopes` | *(none — not even authentication)* | All `Scope` rows |
| `userRoleScopesForUser(userId)` | *(none — not even authentication)* | |

**Open gap:** unlike the mutations, the query fields carry no `[Authorize]`, so they answer
anonymous callers. `checkAuthorization(input)` (ask whether a user id has a given permission)
has been removed for now, along with the `[RequirePermission(...)]` attribute on every query
field — same temporary state as the mutations' note. `PermissionQueries`/`RoleQueries`/
`ScopeQueries`/`UserRoleScopeQueries` still call `.ValueOrThrow()`/`.ValueOrDefault()` on the
`Result` the service returns (see "Error handling" above); re-adding `[Authorize]` and
`[RequirePermission("...")]` to a field is a one-line change each.

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
- **Bootstrap gap, not yet fixed:** since every gate is a permission check rather than a
  structural role check, each `authorization.*` permission that's actually enforced must exist
  in the database, with a rule granting it (e.g. to the `Admin` role), before **anyone —
  including a freshly-bootstrapped Admin — can call the mutation it gates**. Right now no
  mutation enforces a permission (see the GraphQL surface's temporary-state notes); this applies
  again as soon as the gates are re-added.
  `bootstrap-admin` only creates a `UserRoleScope`; it does not create any `Permission` rows,
  and `DatabaseSeeder` currently only seeds `AuthorizationSeedData.UserRoleScopes` (an empty
  hardcoded list by default), not `Permission`/`PermissionRule` — unlike `Role`/`Scope`, which
  do ship default rows in the `InitialCreate` migration (see Persistence above). Until this
  seeding gap is closed (or these rows are inserted by hand on first deploy), `POST /permissions`
  is circularly locked — nothing can call it, because calling it requires a permission only it
  can grant.
