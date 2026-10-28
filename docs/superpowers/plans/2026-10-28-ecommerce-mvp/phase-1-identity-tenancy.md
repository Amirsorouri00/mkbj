# Phase 1 — Identity & Tenancy Implementation Plan

> Part of docs/superpowers/plans/2026-10-28-ecommerce-mvp.md — read its Global Constraints, Review Focus and Cross-phase contracts first.

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development (or
> superpowers:executing-plans). Steps use `- [ ]` checkboxes. Every task ends green and committed.

**Goal:** Humans authenticate through two production-grade Keycloak realms whose roles map to a
seeded permission matrix. Partners authenticate with peppered API keys (`pk_…`) that can be
scoped, rate-limited, rotated, revoked, suspended and usage-logged. Settings hold the seller
identity, a versioned payment instruction and commerce limits. Customer is rebuilt for website and
partner-linked customers with contact-change audit. Phase 2+ code against the ports produced here.

**Spec sections:** §5.1 Partners, §5.2 Customer, §5.7 Settings, §6.1 Keycloak, §6.2 Partner API
keys, §6.3 (`INSUFFICIENT_SCOPE`, `RATE_LIMITED`, `CUSTOMER_CONTACT_REQUIRED`), §3.4 ADR-0021
(written in phase 0; this phase implements it), PRD §3, §7, §12 acceptance 13.

**Assumes phase 0 is done:** Npgsql + snake_case; Payment/Location/Notification deleted; Customer
module is an empty compiling shell; `EwaysVNext.Storage`, `EwaysVNext.Jobs`, `EwaysVNext.Audit`;
`AdminActor`, `PartnerPrincipal`, `PartnerScopes` in `EwaysVNext.Core.Application`;
`Error(Code, Message, HttpStatus, Retryable)`; `X-Correlation-Id` middleware; `[Snowflake]`;
`tests/IntegrationTests/PostgresFixture.cs` (Testcontainers, applies migrations) and its xUnit
collection; `tests/ArchitectureTests/HexagonalBoundaryTests.cs` with `ModuleNames` and the
exception theory; `build/keycloak/*.json` stubs; `build/compose/*`. Where phase 0 picked a
different file name for one of these, use phase 0's name — do not create a duplicate.

**Test commands.** `dotnet test --project <csproj> --filter "FullyQualifiedName~<Name>"` (the
convention used by earlier plans in this repo). If the xUnit v3 runner rejects `--filter`, use
`--filter-class "*<Name>"`. Integration tests need Docker running.

---

## File Structure

### Building blocks and cross-cutting
| Path | Responsibility |
|---|---|
| `src/BuildingBlocks/Core/Application/Security/EwaysClaimTypes.cs` (new) | Claim-name constants: `tenant`, `sub`, `email`, `email_verified`, `partner_id`, `api_key_id`, `scope`, `rate_limit_per_minute`, `bulk_max_items` |
| `src/BuildingBlocks/Core/Application/Security/AdminRoles.cs` (new) | `super_admin`, `staff`, realm names `Shop`, `Backoffice` |
| phase 0's `AdminActor.cs` (modify) | add `TryFrom(ClaimsPrincipal, out AdminActor)` |
| phase 0's `PartnerScopes.cs` (modify) | add `All` list if absent |
| `src/BuildingBlocks/Presentation.Endpoint/Security/AuthPolicies.cs` (new) | Policy-name constants and `Partner(scope)` |
| `src/BuildingBlocks/Presentation.Endpoint/Security/AuthSchemes.cs` (new) | `PartnerApiKey` scheme name, `pk_` prefix, `IsPartnerKey(HttpRequest)` |
| `src/BuildingBlocks/Presentation.Endpoint/Security/AuthorizationPolicyRegistration.cs` (new) | `AddEwaysAuthorizationPolicies()` registers every policy |
| `src/BuildingBlocks/Presentation.Endpoint/Security/PartnerScopeRequirement.cs` (new) | Requirement + handler; fails with the `INSUFFICIENT_SCOPE` reason |
| `src/BuildingBlocks/Presentation.Endpoint/Security/EwaysAuthorizationResultHandler.cs` (new) | Writes the standard error body for 401, 403 and `INSUFFICIENT_SCOPE` |
| `src/Authorization/Authorization.Abstractions/Policies/AdminPermissions.cs` (new) | Permission codes of the spec §6.1 matrix |
| `src/Authorization/Authorization.Implementation/Seeding/AdminPermissionMatrix.cs` (new) | Matrix rows (code, role, fixed GUIDs) used by `HasData` |
| `src/Authorization/**` (modify) | Roles `staff`/`super_admin`, token-role mapping in the check service, gate the RBAC GraphQL |
| `src/Host/EwaysVNext.Host/Program.cs` (modify) | Policies, Partners/Settings modules, middleware order, Serilog `ReadFrom.Services` |
| `src/Host/EwaysVNext.Host/RateLimiting/RateLimitingSetup.cs` (new) | Global limiter: partner partition at the partner's limit, else IP |
| `src/Host/EwaysVNext.Host/Authentication/Extensions/KeycloakAuthenticationExtensions.cs` (modify) | Route `Bearer pk_…` to `PartnerApiKey` before JWT parsing |
| `build/keycloak/shop-realm.json`, `build/keycloak/backoffice-realm.json` | Full realm exports (production configuration) |
| `build/compose/docker-compose.yml`, `docker-compose.dev.yml`, `.env.example` (modify) | Keycloak SMTP `${VAR:?}` fail-fast; mailpit in dev |

### Partners module (new, `src/Modules/Partners/`)
| Path | Responsibility |
|---|---|
| `Application/Partners/Domain/{Partner,ApiKey,PartnerStatus,PartnerLimits}.cs` | Aggregate + child; scopes, limits, issue/rotate/revoke, suspend |
| `Application/Partners/Keys/{ApiKeyFormat,ApiKeyHasher,ApiKeyPepper,ApiKeyOptions}.cs` | Key generation/parsing, HMAC-SHA256 with pepper |
| `Application/Partners/Ports/Inbound/{IPartnerAdminService,IApiKeyAuthenticator,IApiKeyUsageRecorder,PartnerCommands}.cs` | Inbound ports |
| `Application/Partners/Ports/Outbound/{IPartnerRepository,IApiKeyLookup}.cs` | Outbound ports |
| `Application/Partners/Services/{PartnerAdminService,ApiKeyAuthenticator}.cs` | Sealed services |
| `Application/Usage/{ApiKeyUsage,ApiKeyUsageEntry}.cs` | Append-only usage row (not an aggregate) |
| `Adapter/Outbound/Persistence/{PartnerRepository,ApiKeyLookup}.cs` | EF adapters |
| `Adapter/Outbound/Usage/{ChannelApiKeyUsageRecorder,ApiKeyUsageFlushService,UsagePruneJobHandler,PartnerUsageOptions}.cs` | Bounded channel, batch insert, 90-day prune |
| `Adapter/Inbound/Rest/Authentication/PartnerApiKeyAuthenticationHandler.cs` | The `PartnerApiKey` scheme |
| `Adapter/Inbound/Rest/Usage/PartnerApiUsageMiddleware.cs` | Records one usage entry per partner request |
| `Adapter/Inbound/Rest/Admin/{PartnerAdminEndpoints,PartnerAdminRequests}.cs` | `/api/v1/admin/partners…` |
| `Adapter/Inbound/GraphQL/{PartnerAdminQueries,PartnerAdminMutations,PartnerGraphQLTypes,DependencyInjection}.cs` | Admin GraphQL |
| `QueryApplication/{IPartnerAdminQueries,PartnerAdminQueries,PartnerAdminShapes,DependencyInjection}.cs` | Admin reads (list, detail with keys, usage) |
| `Installer/ModuleInstaller.cs` | `AddPartnersModule(IConfiguration)`, `UsePartnersModule()` |
| `src/Database/Partners/Configurations/{PartnerConfiguration,ApiKeyUsageConfiguration}.cs` | Tables `partners.partners`, `partners.api_keys`, `partners.api_key_usage` |

### Settings module (new, `src/Modules/Settings/`)
| Path | Responsibility |
|---|---|
| `Application/Settings/Domain/{SellerProfile,PaymentInstruction,PaymentInstructionVersion,CommerceLimits,BuyerFieldRules,SettingsDefaults}.cs` | Three singleton aggregates and their defaults |
| `Application/Settings/Ports/Inbound/{ISettingsPort,ISettingsAdminService,SettingsCommands}.cs` | Contract port + admin port |
| `Application/Settings/Ports/Outbound/ISettingsRepository.cs` | Load/save singletons |
| `Application/Settings/Services/{SettingsPortService,SettingsAdminService,FxAgeOptions}.cs` | Services |
| `Adapter/Outbound/Persistence/SettingsRepository.cs`, `Adapter/Inbound/Rest/Admin/SettingsAdminEndpoints.cs`, `Adapter/Inbound/GraphQL/*`, `QueryApplication/*`, `Installer/ModuleInstaller.cs` | Usual layers |
| `src/Database/Settings/Configurations/*.cs` | Tables + seeded defaults (`HasData`) |

### Customer module (rebuilt, `src/Modules/Customer/`)
| Path | Responsibility |
|---|---|
| `Application/Customers/Domain/{Customer,CustomerKind,CustomerStatus,ContactChange,ContactField,Phone,Address,ContactUpdate,ProfileUpdate}.cs` | Aggregate, child, value objects |
| `Application/Customers/Ports/Inbound/{ICustomerResolutionPort,IWebsiteCustomerPort,ICustomerProfileService,ICustomerAdminService,CustomerContracts}.cs` | Contract ports + records |
| `Application/Customers/Ports/Outbound/ICustomerRepository.cs` | Persistence port |
| `Application/Customers/Services/{CustomerResolutionService,WebsiteCustomerService,CustomerProfileService,CustomerAdminService}.cs` | Services |
| `Adapter/Outbound/Persistence/CustomerRepository.cs` | EF adapter |
| `Adapter/Inbound/Rest/Shop/{ShopIdentity,ShopMeEndpoints,ShopRequests}.cs`, `Adapter/Inbound/Rest/Admin/CustomerAdminEndpoints.cs` | REST |
| `Adapter/Inbound/GraphQL/Shop/{MeQueries,ProfileMutations}.cs`, `Adapter/Inbound/GraphQL/Admin/{CustomerAdminQueries,CustomerAdminMutations}.cs`, `CustomerGraphQLTypes.cs` | GraphQL |
| `QueryApplication/Customers/{ICustomerShopQueries,ICustomerAdminQueries,CustomerQueries,CustomerShapes}.cs` | Reads |
| `src/Database/Customer/Configurations/CustomerConfiguration.cs` | Table, filtered uniques, check constraint |

### Tests
| Path | Responsibility |
|---|---|
| `tests/UnitTests/Identity/*`, `tests/UnitTests/Partners/*`, `tests/UnitTests/Settings/*`, `tests/UnitTests/Customers/*` | Pure tests |
| `tests/IntegrationTests/Support/{MutableClock,CapturingSink,PartnerSeeder,BearerClientExtensions}.cs` | Test helpers |
| `tests/IntegrationTests/Partners/PartnerProbeEndpoints.cs` | Test-only partner endpoints, one per scope |
| `tests/IntegrationTests/Partners/*Tests.cs`, `Settings/*Tests.cs`, `Customers/*Tests.cs`, `Identity/Acceptance13StaffPermissionTests.cs` | Integration |
| `tests/ArchitectureTests/PartnerTenancyArchitectureTests.cs` | `No_Inbound_Request_Dto_Has_A_PartnerId_Property` |

---

## Task 1: Admin roles, permission matrix, policies and `AdminActor`

**Files:**
- Create: `src/BuildingBlocks/Core/Application/Security/EwaysClaimTypes.cs`, `src/BuildingBlocks/Core/Application/Security/AdminRoles.cs`
- Modify: phase 0's `AdminActor.cs` and `PartnerScopes.cs` (in `src/BuildingBlocks/Core/Application`)
- Create: `src/BuildingBlocks/Presentation.Endpoint/Security/AuthPolicies.cs`, `AuthSchemes.cs`, `AuthorizationPolicyRegistration.cs`
- Modify: `src/BuildingBlocks/Presentation.Endpoint/EwaysVNext.Presentation.Endpoint.csproj` (reference `Core/Application`)
- Create: `src/Authorization/Authorization.Abstractions/Policies/AdminPermissions.cs`, `src/Authorization/Authorization.Implementation/Seeding/AdminPermissionMatrix.cs`
- Modify: `src/Authorization/Authorization.Implementation/Persistence/Configurations/{RoleConfiguration,PermissionConfiguration,PermissionRuleConfiguration}.cs`
- Modify: `src/Authorization/Authorization.Abstractions/Authorization/Requests/CheckAuthorizationRequest.cs`, `src/Authorization/Authorization.Implementation/Services/AuthorizationCheckService.cs`, `src/Authorization/Authorization.Implementation/Handlers/PermissionAuthorizationHandler.cs`
- Modify: every resolver class in `src/Authorization/Authorization.Endpoints/{Permissions,Roles,Scopes,UserRoleScopes}/*Mutations.cs` and `*Queries.cs`
- Modify: `src/Authorization/README.md` (role mapping, matrix, gates; delete the "temporary state" and "Open gap" notes this closes)
- Create migration: `src/Authorization/Authorization.Implementation/Persistence/Migrations/<ts>_AdminPermissionMatrix.cs`
- Modify: `src/Host/EwaysVNext.Host/Program.cs`, `src/Host/EwaysVNext.Host/Authentication/KeycloakClaimTypes.cs`
- Modify: `tests/IntegrationTests/AdminJwtFactory.cs`; `tests/IntegrationTests/PostgresFixture.cs` (only if it does not already migrate `AuthorizationDbContext`)
- Test: `tests/UnitTests/Identity/AdminActorTests.cs`, `tests/UnitTests/Identity/AdminPermissionMatrixTests.cs`

**Interfaces:**
- Consumes: `AdminActor(Guid UserId, bool IsSuperAdmin)`, `PartnerScopes` (phase 0).
- Produces:
```csharp
// EwaysVNext.Core.Application.Security
public static class EwaysClaimTypes { Tenant="tenant"; Subject="sub"; Email="email"; EmailVerified="email_verified";
  PartnerId="partner_id"; ApiKeyId="api_key_id"; Scope="scope"; RateLimitPerMinute="rate_limit_per_minute"; BulkMaxItems="bulk_max_items"; }
public static class AdminRoles { SuperAdmin="super_admin"; Staff="staff"; ShopRealm="Shop"; BackofficeRealm="Backoffice"; }
// AdminActor (extend)
public static bool TryFrom(ClaimsPrincipal user, out AdminActor actor); // tenant=Backoffice, sub is a Guid, role staff|super_admin
// PartnerScopes (extend if absent)
public static readonly IReadOnlyList<string> All;

// EwaysVNext.Presentation.Endpoint.Security
public static class AuthPolicies {
  public const string AdminStaff = "admin:staff";   // Backoffice tenant + role staff OR super_admin
  public const string AdminSuper = "admin:super";   // Backoffice tenant + role super_admin
  public const string ShopCustomer = "shop:customer"; // Shop tenant
  public const string PartnerPrefix = "partner:";
  public static string Partner(string scope) => PartnerPrefix + scope; // e.g. "partner:orders:create"
}
public static class AuthSchemes { public const string PartnerApiKey = "PartnerApiKey"; public const string PartnerKeyPrefix = "pk_";
  public static bool IsPartnerKey(HttpRequest request); } // "Bearer pk_…" (scheme word case-insensitive, prefix ordinal)
public static class AuthorizationPolicyRegistration { public static AuthorizationOptions AddEwaysAuthorizationPolicies(this AuthorizationOptions o); }

// AuthorizationService.Abstractions.Policies
public static class AdminPermissions {
  // staff + super_admin
  CatalogManage="catalog.manage"; ImportsManage="imports.manage"; CustomersManage="customers.manage";
  OrdersManage="orders.manage"; PaymentsConfirm="payments.confirm";
  // super_admin only
  PartnersManage="partners.manage"; FxManage="fx.manage"; CurrenciesManage="currencies.manage";
  SettingsManage="settings.manage"; RefundsManage="refunds.manage"; AdminAccountsManage="admin-accounts.manage";
}
public record CheckAuthorizationRequest(Guid UserId, string PermissionCode, Guid? ScopeId,
  IReadOnlyCollection<string>? TokenRoleCodes = null);
```

**How later phases gate an admin endpoint (record this in the README edit):** the group gets
`.RequireAuthorization(AuthPolicies.AdminStaff)` (or `AdminSuper` for super-only areas), and each
endpoint gets `.RequirePermission(AdminPermissions.X)`. GraphQL uses
`[Authorize(Policy = AuthPolicies.AdminStaff|AdminSuper)]` plus `[RequirePermission(AdminPermissions.X)]`.
The role policy is the realm gate. It stays even though the permission alone would refuse, because
permission rows are editable data.

- [ ] **Step 1: Write the failing tests.**
```csharp
// tests/UnitTests/Identity/AdminActorTests.cs
[Theory]
[InlineData("Backoffice", "super_admin", true, true)]
[InlineData("Backoffice", "staff", true, false)]
[InlineData("Shop", "super_admin", false, false)]   // a Shop token never becomes an admin
[InlineData("Backoffice", "customer", false, false)]
public void TryFrom_Reads_Tenant_Sub_And_Role(string tenant, string role, bool ok, bool isSuper)
{
    var sub = Guid.NewGuid();
    var user = Principal(("tenant", tenant), ("sub", sub.ToString()), (ClaimTypes.Role, role));
    AdminActor.TryFrom(user, out var actor).Should().Be(ok);
    if (ok) actor.Should().Be(new AdminActor(sub, isSuper));
}
[Fact] public void TryFrom_Rejects_A_Non_Guid_Sub() { /* sub "abc" → false */ }

// tests/UnitTests/Identity/AdminPermissionMatrixTests.cs — AuthorizationDbContext on EF InMemory,
// EnsureCreated() applies HasData; the handler is driven with a ClaimsPrincipal.
[Theory]
[InlineData("staff", AdminPermissions.CatalogManage, true)]
[InlineData("staff", AdminPermissions.ImportsManage, true)]
[InlineData("staff", AdminPermissions.CustomersManage, true)]
[InlineData("staff", AdminPermissions.OrdersManage, true)]
[InlineData("staff", AdminPermissions.PaymentsConfirm, true)]
[InlineData("staff", AdminPermissions.PartnersManage, false)]
[InlineData("staff", AdminPermissions.FxManage, false)]
[InlineData("staff", AdminPermissions.CurrenciesManage, false)]
[InlineData("staff", AdminPermissions.SettingsManage, false)]
[InlineData("staff", AdminPermissions.RefundsManage, false)]
[InlineData("staff", AdminPermissions.AdminAccountsManage, false)]
[InlineData("super_admin", AdminPermissions.PartnersManage, true)]
[InlineData("super_admin", AdminPermissions.RefundsManage, true)]
[InlineData("super_admin", AdminPermissions.CatalogManage, true)]
public async Task Backoffice_Role_Claim_Maps_To_Seeded_Matrix(string role, string permission, bool allowed)
    => (await Evaluate(tenant: "Backoffice", role, permission)).Should().Be(allowed);

[Fact]
public async Task Roles_From_A_Shop_Token_Are_Ignored()
    => (await Evaluate(tenant: "Shop", "super_admin", AdminPermissions.CatalogManage)).Should().BeFalse();

[Fact]
public void Matrix_Covers_Every_AdminPermissions_Constant()
    => AdminPermissionMatrix.Rows.Select(r => r.Code).Should().BeEquivalentTo(
        typeof(AdminPermissions).GetFields().Select(f => (string)f.GetRawConstantValue()!));
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~Identity"`. Expected: FAIL (does not compile: `TryFrom`, `AdminPermissions`, `AdminPermissionMatrix` missing).
- [ ] **Step 3: Implement.**
  - `EwaysClaimTypes`, `AdminRoles`, `AdminActor.TryFrom` (role read from `ClaimTypes.Role`, which `KeycloakAuthenticationExtensions.ValidateKeycloakAccessToken` adds). If phase 0's `PartnerPrincipal.TryFrom` uses string literals, switch it to `EwaysClaimTypes` (same values). `KeycloakClaimTypes.Tenant => EwaysClaimTypes.Tenant`.
  - `AdminPermissionMatrix.Rows`: `sealed record MatrixRow(string Code, string Name, string RoleCode, Guid PermissionId, Guid RuleId)`. One row per `AdminPermissions` constant; the five staff-area rows use `RoleCode = "staff"`, the six others `"super_admin"`. GUIDs are hard-coded literals (generate once, commit; never compute at runtime).
  - `RoleConfiguration.HasData`: add `staff` (Rank 10) and `super_admin` (Rank 20) with fixed GUIDs. The existing rank rule (`assigned.Rank >= rule.Rank`) makes `super_admin` satisfy every staff rule. Keep the existing `User/Manager/Admin` rows.
  - `PermissionConfiguration`/`PermissionRuleConfiguration.HasData` from `AdminPermissionMatrix.Rows` (anonymous objects, `SeedTimestamp` constant, no rule scopes).
  - `PermissionAuthorizationHandler`: pass `TokenRoleCodes` = the `ClaimTypes.Role` values **only when** the `tenant` claim is `Backoffice`; otherwise `null`.
  - `AuthorizationCheckService.CheckAsync`: resolve `TokenRoleCodes` to ranks via `db.Roles.Where(r => codes.Contains(r.Code))`. Treat each as an assignment with no scope; it satisfies a rule when `rank >= rule.RoleRank` and the rule has no scopes. Union with the existing `UserRoleScopes` assignments.
  - `AddEwaysAuthorizationPolicies`: `AdminStaff`, `AdminSuper`, `ShopCustomer` as described above (all `RequireAuthenticatedUser()`). Partner policies are added in Task 6.
  - `Program.cs`: replace the inline `"Admin"`/`"Customer"` policies with `opts.AddEwaysAuthorizationPolicies()`. Keep the two legacy names as aliases (`"Admin"` = same requirements as `AdminStaff`, `"Customer"` = `ShopCustomer` or `AdminStaff`) with a `// removed in phase 2–3` comment, because Catalog endpoints still reference `"Admin"`.
  - Authorization GraphQL: add `[Authorize(Policy = AuthPolicies.AdminSuper)]` to every mutation and `[Authorize(Policy = AuthPolicies.AdminStaff)]` to every query field. The RBAC mutations are otherwise ungated, which would let staff grant themselves `partners.manage`.
  - Migration: `dotnet ef migrations add AdminPermissionMatrix -p src/Authorization/Authorization.Implementation/Authorization.Implementation.csproj -s src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj -c AuthorizationDbContext -o Persistence/Migrations`.
  - `AdminJwtFactory`: `CreateToken(tenant, role, Guid? sub = null, IEnumerable<Claim>? extra = null)`; add `CreateSuperAdminToken()`, `CreateStaffToken()`, `CreateShopToken(Guid sub, string email, bool emailVerified)`; `CreateAdminToken()` now mints `super_admin`.
  - `PostgresFixture.InitializeAsync`: also `MigrateAsync()` the `AuthorizationDbContext` if it does not already.
- [ ] **Step 4: Run** the Step 2 command, then `dotnet build EwaysVNext.slnx`. Expected: PASS, build clean.
- [ ] **Step 5: Mutation check.** Change `staff` to `super_admin` on one staff row → `Backoffice_Role_Claim_Maps_To_Seeded_Matrix` fails. Remove the tenant check in the handler → `Roles_From_A_Shop_Token_Are_Ignored` fails. Restore both.
- [ ] **Step 6: Commit.**
```bash
git add src/BuildingBlocks src/Authorization src/Host tests/UnitTests/Identity tests/IntegrationTests
git commit -m "feat(identity): map Backoffice roles to seeded permission matrix; admin policies" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 2: Keycloak realm exports and SMTP fail-fast

**Files:**
- Modify (replace stub): `build/keycloak/shop-realm.json`, `build/keycloak/backoffice-realm.json`
- Modify: `build/compose/docker-compose.yml` (keycloak service env), `build/compose/docker-compose.dev.yml` (add mailpit), `build/compose/.env.example`
- Test: `tests/UnitTests/Identity/KeycloakRealmExportTests.cs`

**Interfaces:**
- Consumes: `src/Host/EwaysVNext.Host/appsettings.json` `Keycloak:Realms` client ids (`modular-monolith` for Shop, `backoffice-console` for Backoffice). They stay unchanged, because `ValidateKeycloakAccessToken` checks `azp`.
- Produces: env vars `KC_SMTP_HOST`, `KC_SMTP_PORT`, `KC_SMTP_FROM`, `KC_SMTP_FROM_DISPLAY_NAME`, `KC_SMTP_USER`, `KC_SMTP_PASSWORD`, `KC_SMTP_STARTTLS`, `KC_SHOP_WEB_ORIGIN`, `KC_ADMIN_WEB_ORIGIN`, `KC_API_PUBLIC_URL`.

**Realm content.** Keycloak substitutes `${VAR}` placeholders at `--import-realm`.

| Setting | Shop | Backoffice |
|---|---|---|
| `realm` / `enabled` | `Shop` / true | `Backoffice` / true |
| `registrationAllowed`, `registrationEmailAsUsername` | true, true | false, false |
| `verifyEmail`, `resetPasswordAllowed`, `loginWithEmailAllowed`, `duplicateEmailsAllowed` | true, true, true, false | false, true, true, false |
| `bruteForceProtected` | true | true |
| `passwordPolicy` | `length(10) and notEmail and notUsername` | `length(14) and notEmail and notUsername` |
| `accessTokenLifespan` / `ssoSessionIdleTimeout` / `ssoSessionMaxLifespan` | 300 / 1800 / 36000 | 300 / 900 / 28800 |
| `roles.realm` | (defaults only) | `super_admin`, `staff` |
| `requiredActions` | `VERIFY_EMAIL` enabled | `CONFIGURE_TOTP` enabled with `defaultAction: true` **[Design call]** |
| client | `modular-monolith`: public, standard flow, PKCE `S256`, `directAccessGrantsEnabled: false`, `redirectUris: ["${KC_SHOP_WEB_ORIGIN}/*", "${KC_API_PUBLIC_URL}/scalar/*"]`, `webOrigins: ["${KC_SHOP_WEB_ORIGIN}"]`, default scopes `profile, email, roles` | `backoffice-console`: same shape with `${KC_ADMIN_WEB_ORIGIN}` |
| `smtpServer` | `host ${KC_SMTP_HOST}`, `port ${KC_SMTP_PORT}`, `from ${KC_SMTP_FROM}`, `fromDisplayName ${KC_SMTP_FROM_DISPLAY_NAME}`, `auth "true"`, `user ${KC_SMTP_USER}`, `password ${KC_SMTP_PASSWORD}`, `starttls ${KC_SMTP_STARTTLS}` | same |

No user, no client secret and no credential value appears in either file. The API exposes no
user-creation endpoint; admin accounts are created in the Keycloak console.

- [ ] **Step 1: Write the failing tests.**
```csharp
public class KeycloakRealmExportTests
{
    [Theory, InlineData("shop-realm.json", "Shop"), InlineData("backoffice-realm.json", "Backoffice")]
    public void Realm_Is_Named_And_Uses_Placeholder_Smtp(string file, string realm)
    {
        var json = Load(file);
        json["realm"]!.GetValue<string>().Should().Be(realm);
        json["accessTokenLifespan"]!.GetValue<int>().Should().Be(300);
        json["smtpServer"]!.AsObject().Select(p => p.Value!.ToString())
            .Where(v => v != "true").Should().OnlyContain(v => v.StartsWith("${KC_SMTP_"));
    }
    [Fact] public void Shop_Requires_Email_Verification_And_Allows_Reset() { /* verifyEmail, resetPasswordAllowed, registrationAllowed == true */ }
    [Fact] public void Backoffice_Defines_Exactly_SuperAdmin_And_Staff_And_No_Registration()
    { /* roles.realm names ⊇ {super_admin, staff}, no other custom role; registrationAllowed == false */ }
    [Theory, InlineData("shop-realm.json", "modular-monolith"), InlineData("backoffice-realm.json", "backoffice-console")]
    public void Client_Is_Public_Pkce_With_Placeholder_Redirects(string file, string clientId)
    { /* publicClient, pkce S256, directAccessGrantsEnabled false, every redirectUri starts with "${" */ }
    [Theory, InlineData("shop-realm.json"), InlineData("backoffice-realm.json")]
    public void Export_Contains_No_Secrets_Or_Users(string file)
    { /* no "users" key; no property named "secret" or "password" whose value does not start with "${" */ }
    [Fact]
    public void Production_Compose_Fails_Fast_On_Missing_Smtp()
    {
        var compose = File.ReadAllText(Repo("build/compose/docker-compose.yml"));
        foreach (var v in new[] { "KC_SMTP_HOST", "KC_SMTP_PORT", "KC_SMTP_FROM", "KC_SMTP_USER", "KC_SMTP_PASSWORD" })
            compose.Should().MatchRegex($@"\$\{{{v}:\?[^}}]+\}}");
    }
}
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~KeycloakRealmExportTests"`. Expected: FAIL (stubs lack the content).
- [ ] **Step 3: Implement** both realm exports per the table. In production compose, the keycloak service passes every `KC_SMTP_*` as `${KC_SMTP_X:?KC_SMTP_X is required}`, so `docker compose up` refuses to start without them. That is the spec's "startup fails fast in Production". Dev compose uses `${KC_SMTP_HOST:-mailpit}`, port `1025`, `KC_SMTP_STARTTLS=false`, and adds `mailpit` (`axllent/mailpit:v1.21`, UI on 8025). `.env.example` lists every new variable with empty values for secrets.
- [ ] **Step 4: Run** the Step 2 command. Expected: PASS. Also `docker compose -f build/compose/docker-compose.yml config` with `KC_SMTP_HOST` unset must exit non-zero (manual check; record the result in the phase report).
- [ ] **Step 5: Commit.**
```bash
git add build/keycloak build/compose tests/UnitTests/Identity/KeycloakRealmExportTests.cs
git commit -m "feat(keycloak): production realm exports for Shop and Backoffice; SMTP fail-fast" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 3: Partners module scaffold, key format and peppered hashing

**Files:**
- Run: `./New-EwaysModule.ps1 -ModuleName Partners` (creates Application, Adapter/Outbound, Adapter/Inbound/Rest, QueryApplication, Installer and adds them to `EwaysVNext.slnx`). Then create `src/Modules/Partners/Adapter/Inbound/GraphQL/EwaysVNext.Modules.Partners.Adapter.Inbound.GraphQL.csproj` by copying the package and project references of Catalog's GraphQL csproj, and add it to the slnx and the Installer.
- Modify: `src/Database/EwaysVNext.Database.csproj` (reference Partners.Application), `tests/UnitTests/EwaysVNext.UnitTests.csproj`, `tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj` (reference Partners.Installer), `tests/ArchitectureTests/HexagonalBoundaryTests.cs` (`ModuleNames` += `"Partners"`)
- Create: `src/Modules/Partners/Application/Partners/Keys/{ApiKeyFormat,ApiKeyHasher,ApiKeyPepper,ApiKeyOptions}.cs`
- Create: `src/Host/EwaysVNext.Host/ModuleAppSetting/Partners/modules.partners.json`, `modules.partners.Development.json`
- Test: `tests/UnitTests/Partners/ApiKeyFormatTests.cs`, `tests/UnitTests/Partners/ApiKeyHasherTests.cs`

**Interfaces:**
```csharp
namespace EwaysVNext.Modules.Partners.Application.Partners.Keys;
public sealed record GeneratedApiKey(string Prefix, string Secret, string Plaintext); // Plaintext = "pk_" + Prefix + "_" + Secret
public static class ApiKeyFormat {
  public const int PrefixLength = 8;   // base62
  public const int SecretLength = 43;  // 32 random bytes, base62, left-padded with '0' (62^43 > 2^256)
  public static GeneratedApiKey Generate();                 // RandomNumberGenerator only
  public static bool TryParse(string presented, out string prefix, out string secret);
}
public sealed record ApiKeyPepper(byte[] Value) {
  public static ApiKeyPepper FromConfiguration(string? raw); // throws InvalidOperationException when null or < 32 chars; UTF-8 bytes
}
public sealed class ApiKeyHasher(ApiKeyPepper pepper) {
  public byte[] Hash(string secret);                        // HMACSHA256.HashData(pepper.Value, UTF8(secret))
  public bool Matches(string secret, byte[] storedHash);    // CryptographicOperations.FixedTimeEquals
}
public sealed class ApiKeyOptions { public TimeSpan RotationOverlap { get; init; } = TimeSpan.FromHours(24);
  public TimeSpan MaxRotationOverlap { get; init; } = TimeSpan.FromDays(30); } // section "ApiKeys"
```
Config: `modules.partners.json` holds `ApiKeys:RotationOverlap = "1.00:00:00"` and no pepper.
`modules.partners.Development.json` holds a clearly-labelled, non-secret dev pepper
(`"dev-only-pepper-not-for-production-0000000000"`). Production gets `ApiKeys__Pepper` from `.env`
(add it to `.env.example`).

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public void Generate_Produces_The_Documented_Format()
{
    var key = ApiKeyFormat.Generate();
    key.Plaintext.Should().MatchRegex("^pk_[0-9A-Za-z]{8}_[0-9A-Za-z]{43}$");
    key.Plaintext.Should().Be($"pk_{key.Prefix}_{key.Secret}");
    ApiKeyFormat.Generate().Secret.Should().NotBe(key.Secret);
}
[Fact] public void TryParse_Round_Trips() { /* Generate → TryParse → same prefix and secret */ }
[Theory]
[InlineData("")] [InlineData("pk_")] [InlineData("PK_ABCDEFGH_0000000000000000000000000000000000000000000")]
[InlineData("pk_ABCDEFG_0000000000000000000000000000000000000000000")]   // prefix 7
[InlineData("pk_ABCDEFGH_000000000000000000000000000000000000000000")]   // secret 42
[InlineData("pk_ABCDEFGH_00000000000000000000000000000000000000000000")] // secret 44
[InlineData("pk_ABCDEFGH_000000000000000000000000000000000000000000-")]  // non-base62
[InlineData("pk_ABC_EFGH_0000000000000000000000000000000000000000000")]  // extra underscore
public void TryParse_Rejects_Malformed(string presented) => ApiKeyFormat.TryParse(presented, out _, out _).Should().BeFalse();

[Fact] public void Hash_Is_Hmac_Sha256_Of_Secret_Keyed_By_Pepper()
{
    var pepper = new ApiKeyPepper(Encoding.UTF8.GetBytes(new string('p', 32)));
    new ApiKeyHasher(pepper).Hash("s3cret").Should().Equal(HMACSHA256.HashData(pepper.Value, "s3cret"u8.ToArray()));
}
[Fact] public void Matches_Fails_Under_A_Different_Pepper() { /* hash with pepper A, Matches with pepper B → false */ }
[Theory, InlineData(null), InlineData(""), InlineData("short-31-chars-0000000000000000")]
public void Pepper_Shorter_Than_32_Chars_Is_Refused(string? raw)
    => FluentActions.Invoking(() => ApiKeyPepper.FromConfiguration(raw)).Should().Throw<InvalidOperationException>();
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~Partners.ApiKey"`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement** the four types above. Base62 alphabet `0-9A-Za-z`. The secret is `BigInteger(bytes, isUnsigned: true, isBigEndian: true)` converted to base62 and left-padded to 43. The prefix is 8 × `RandomNumberGenerator.GetInt32(62)`. `TryParse` checks exact lengths, the alphabet, and exactly two `_` separators.
- [ ] **Step 4: Run** the Step 2 command and `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`. Expected: PASS (the empty Partners projects satisfy the boundary rules).
- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Partners src/Database/EwaysVNext.Database.csproj src/Host/EwaysVNext.Host/ModuleAppSetting/Partners EwaysVNext.slnx tests build/compose/.env.example
git commit -m "feat(partners): scaffold module; API key format and HMAC-SHA256 pepper hashing" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 4: `Partner` aggregate and `ApiKey` child

**Files:**
- Create: `src/Modules/Partners/Application/Partners/Domain/{Partner,ApiKey,PartnerStatus,PartnerLimits}.cs`
- Test: `tests/UnitTests/Partners/PartnerDomainTests.cs`

**Interfaces:**
```csharp
public enum PartnerStatus { Active, Suspended }
public static class PartnerLimits { RateLimitMin=1; RateLimitMax=6000; DefaultRateLimitPerMinute=300; BulkMin=1; BulkMax=1000; DefaultBulkMaxItems=100; NameMaxLength=200; }
public sealed class Partner : AggregateRoot<long> {
  public string Name; public PartnerStatus Status; public IReadOnlySet<string> Scopes; public int RateLimitPerMinute; public int BulkMaxItems;
  public DateTimeOffset CreatedAt; public IReadOnlyList<ApiKey> Keys;
  public static Partner Create(long id, string name, IReadOnlyCollection<string> scopes, int rateLimitPerMinute, int bulkMaxItems, DateTimeOffset now);
  public void Suspend(); public void Reactivate();
  public void SetScopes(IReadOnlyCollection<string> scopes);           // trims every key's scopes to the new set
  public void SetLimits(int rateLimitPerMinute, int bulkMaxItems);
  public ApiKey IssueKey(long keyId, string prefix, byte[] secretHash, IReadOnlyCollection<string>? scopes, DateTimeOffset? expiresAt, DateTimeOffset now); // null scopes = all partner scopes
  public ApiKey RotateKey(long oldKeyId, long newKeyId, string prefix, byte[] secretHash, TimeSpan overlap, DateTimeOffset now);
  public void RevokeKey(long keyId, DateTimeOffset now);              // idempotent
}
public sealed class ApiKey : Entity<long> {
  public long PartnerId; public string Prefix; public byte[] SecretHash; public IReadOnlySet<string> Scopes;
  public DateTimeOffset CreatedAt; public DateTimeOffset? ExpiresAt; public DateTimeOffset? RevokedAt;
  public bool IsUsableAt(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || now < ExpiresAt);
}
```
Violations throw `DomainException` with code `VALIDATION_FAILED` (bad name, scope, limit or
expiry), `NOT_FOUND` (unknown key id) or `CONFLICT` (rotating a key that is not usable).
`LastUsedAt` is **not** a stored field. Task 8 derives it from the usage log (see Spec conflicts
in the phase report).

- [ ] **Step 1: Write the failing tests** (`TestTime.Now` as `now`).
```csharp
[Fact] public void Create_Rejects_An_Unknown_Scope()
    => Invoking(() => Partner.Create(1, "Acme", ["orders:delete"], 300, 100, Now)).Should().Throw<DomainException>().Which.Code.Should().Be("VALIDATION_FAILED");
[Theory, InlineData(0, 100), InlineData(6001, 100), InlineData(300, 0), InlineData(300, 1001)]
public void Create_Enforces_Limit_Bounds(int rate, int bulk) { /* throws VALIDATION_FAILED */ }
[Fact] public void IssueKey_Defaults_To_All_Partner_Scopes() { /* partner {orders:create, orders:read}; IssueKey(null) → same set */ }
[Fact] public void IssueKey_Rejects_Scopes_Outside_The_Partner() { /* partner {orders:read}; key {orders:create} → VALIDATION_FAILED */ }
[Fact] public void IssueKey_Rejects_Expiry_In_The_Past() { /* expiresAt = Now → VALIDATION_FAILED */ }
[Fact] public void Rotate_Gives_Old_Key_Overlap_Expiry_And_New_Key_Same_Scopes()
{
    var p = PartnerWithKey(keyId: 10, scopes: ["orders:create"]);
    var fresh = p.RotateKey(10, 11, "NEWPREF1", Hash(), TimeSpan.FromHours(24), Now);
    p.Keys.Single(k => k.Id == 10).ExpiresAt.Should().Be(Now.AddHours(24));
    fresh.Scopes.Should().BeEquivalentTo(["orders:create"]);
    fresh.ExpiresAt.Should().BeNull();
}
[Fact] public void Rotate_Never_Extends_An_Earlier_Expiry() { /* old ExpiresAt = Now+10m, overlap 24h → stays Now+10m */ }
[Fact] public void Rotate_A_Revoked_Key_Is_A_Conflict() { /* Code == "CONFLICT" */ }
[Fact] public void Revoke_Is_Idempotent() { /* revoke at Now, revoke again at Now+1h → RevokedAt == Now */ }
[Fact] public void SetScopes_Trims_Existing_Key_Scopes() { /* key {orders:create, orders:read}; SetScopes([orders:read]) → key {orders:read} */ }
[Fact] public void Key_Is_Not_Usable_At_Its_Exact_Expiry()
{ /* ExpiresAt = Now+1h: IsUsableAt(Now+1h-1tick) true, IsUsableAt(Now+1h) false */ }
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~PartnerDomainTests"`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement** the aggregate. Scopes are validated against `PartnerScopes.All`. The name is trimmed, non-empty and ≤ 200 characters. `RotateKey` sets `old.ExpiresAt = Min(old.ExpiresAt ?? ∞, now + overlap)`.
- [ ] **Step 4: Run** the Step 2 command. Expected: PASS.
- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Partners/Application tests/UnitTests/Partners/PartnerDomainTests.cs
git commit -m "feat(partners): Partner aggregate with key issue, rotate-with-overlap, revoke, suspend" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 5: Partner persistence, `IPartnerAdminService` and `IApiKeyAuthenticator`

**Files:**
- Create: `src/Modules/Partners/Application/Partners/Ports/Inbound/{IPartnerAdminService,IApiKeyAuthenticator,PartnerCommands}.cs`, `Ports/Outbound/{IPartnerRepository,IApiKeyLookup}.cs`, `Services/{PartnerAdminService,ApiKeyAuthenticator}.cs`, `Application/DependencyInjection.cs`
- Create: `src/Modules/Partners/Adapter/Outbound/Persistence/{PartnerRepository,ApiKeyLookup}.cs`, `Adapter/Outbound/DependencyInjection.cs`
- Create: `src/Database/Partners/Configurations/PartnerConfiguration.cs`; modify `src/Database/EwaysVNextDbContext.cs` (`DbSet<Partner> Partners`)
- Modify: `src/Modules/Partners/Installer/ModuleInstaller.cs` (`AddPartnersModule(IConfiguration)`), `src/Host/EwaysVNext.Host/Program.cs` (call it; add `"partners"` to `AddModuleConfiguration`), Host csproj reference
- Create migration: `dotnet ef migrations add Partners -p src/Database -s src/Host/EwaysVNext.Host`
- Test: `tests/UnitTests/Partners/ApiKeyAuthenticatorTests.cs`, `tests/UnitTests/Partners/PartnerAdminServiceTests.cs`

**Interfaces:**
- Consumes: `IAuditTrail`, `AdminActor`, `IClock`, `IIdGenerator`, `IUnitOfWork`.
- Produces (contract names exact):
```csharp
public interface IApiKeyAuthenticator { Task<ApiKeyAuthResult?> AuthenticateAsync(string presentedKey, CancellationToken ct = default); }
public sealed record ApiKeyAuthResult(long PartnerId, long ApiKeyId, IReadOnlySet<string> Scopes, int RateLimitPerMinute, int BulkMaxItems);

public sealed record CreatePartnerCommand(string Name, IReadOnlyCollection<string> Scopes, int? RateLimitPerMinute, int? BulkMaxItems);
public sealed record IssuedApiKey(long KeyId, string Prefix, string PlaintextKey, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt);
public interface IPartnerAdminService {   // every method: Super Admin only (Error.Forbidden otherwise), audited in the same save
  Task<Result<long>> CreateAsync(CreatePartnerCommand command, AdminActor actor, CancellationToken ct = default);
  Task<Result> SuspendAsync(long partnerId, AdminActor actor, CancellationToken ct = default);
  Task<Result> ReactivateAsync(long partnerId, AdminActor actor, CancellationToken ct = default);
  Task<Result> SetScopesAsync(long partnerId, IReadOnlyCollection<string> scopes, AdminActor actor, CancellationToken ct = default);
  Task<Result> SetLimitsAsync(long partnerId, int rateLimitPerMinute, int bulkMaxItems, AdminActor actor, CancellationToken ct = default);
  Task<Result<IssuedApiKey>> IssueKeyAsync(long partnerId, IReadOnlyCollection<string>? scopes, DateTimeOffset? expiresAt, AdminActor actor, CancellationToken ct = default);
  Task<Result<IssuedApiKey>> RotateKeyAsync(long partnerId, long keyId, TimeSpan? overlap, AdminActor actor, CancellationToken ct = default);
  Task<Result> RevokeKeyAsync(long partnerId, long keyId, AdminActor actor, CancellationToken ct = default);
}
public interface IPartnerRepository { Task<Partner?> GetAsync(long partnerId, CancellationToken ct = default); void Add(Partner partner); }
public sealed record ApiKeyCredential(long KeyId, long PartnerId, byte[] SecretHash, IReadOnlySet<string> KeyScopes,
  DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt, bool PartnerActive, int RateLimitPerMinute, int BulkMaxItems);
public interface IApiKeyLookup { Task<ApiKeyCredential?> FindByPrefixAsync(string prefix, CancellationToken ct = default); } // AsNoTracking projection, one indexed query
```
**Persistence:** `partners.partners` (`scopes text[]`, `status` stored as string, `xmin`
concurrency token, `ValueGeneratedNever`). `partners.api_keys` is mapped with `HasMany(Keys)`:
`prefix varchar(8)` with a **unique index**, `secret_hash bytea`, `scopes text[]`,
`CHECK (expires_at IS NULL OR expires_at > created_at)`.

**Audit:** actions `partner.created|suspended|reactivated|scopes_set|limits_set|key_issued|key_rotated|key_revoked`,
target type `Partner`, target id = partner id. `before`/`after` carry only the changed fields;
key payloads are `{ keyId, prefix, scopes, expiresAt }`, never the plaintext or the hash.

- [ ] **Step 1: Write the failing tests** (NSubstitute for `IApiKeyLookup`, `IPartnerRepository`, `IAuditTrail`, `IUnitOfWork`; `FixedClock`).
```csharp
// ApiKeyAuthenticatorTests
[Fact] public async Task Valid_Key_Returns_Partner_Scopes_And_Limits() { /* credential for prefix, hash matches → result(PartnerId, KeyId, scopes, 300, 100) */ }
[Fact] public async Task Wrong_Secret_Returns_Null() { }
[Fact] public async Task Malformed_Key_Returns_Null_Without_Lookup() { await sut.AuthenticateAsync("pk_bad"); await lookup.DidNotReceiveWithAnyArgs().FindByPrefixAsync(default!); }
[Fact] public async Task Unknown_Prefix_Returns_Null() { }
[Fact] public async Task Revoked_Key_Returns_Null() { }
[Fact] public async Task Key_At_Exact_Expiry_Returns_Null() { /* ExpiresAt == clock.UtcNow */ }
[Fact] public async Task Suspended_Partner_Returns_Null() { /* PartnerActive false */ }

// PartnerAdminServiceTests
[Fact] public async Task Staff_Actor_Is_Forbidden_And_Nothing_Is_Touched()
{
    var result = await sut.CreateAsync(new("Acme", ["orders:create"], null, null), new AdminActor(Guid.NewGuid(), IsSuperAdmin: false));
    result.Error!.HttpStatus.Should().Be(403);
    repository.ReceivedCalls().Should().BeEmpty(); await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
}
[Fact] public async Task Create_Uses_Spec_Defaults() { /* null limits → RateLimitPerMinute 300, BulkMaxItems 100 */ }
[Fact] public async Task IssueKey_Returns_Plaintext_Once_And_Stores_Only_The_Hash()
{
    var issued = (await sut.IssueKeyAsync(partner.Id, null, null, Super)).Value!;
    ApiKeyFormat.TryParse(issued.PlaintextKey, out var prefix, out var secret).Should().BeTrue();
    var stored = partner.Keys.Single();
    stored.Prefix.Should().Be(prefix);
    stored.SecretHash.Should().Equal(hasher.Hash(secret));
    audit.ReceivedCalls().SelectMany(c => c.GetArguments()).Select(a => JsonSerializer.Serialize(a))
        .Should().NotContain(s => s.Contains(secret) || s.Contains(Convert.ToBase64String(stored.SecretHash)));
}
[Fact] public async Task Rotate_Uses_Configured_Overlap_When_None_Given() { /* options 24h → old ExpiresAt = now+24h */ }
[Fact] public async Task Rotate_Rejects_Overlap_Above_Max() { /* 31 days → VALIDATION_FAILED */ }
[Fact] public async Task Unknown_Partner_Is_Not_Found() { /* HttpStatus 404, code NOT_FOUND */ }
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~Partners"`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement.** The services are `sealed` and follow `load → mutate → audit.Record → one SaveChangesAsync`. A `DomainException` becomes `Result.Failure(new Error(ex.Code, ex.Message))`. `ApiKeyAuthenticator` runs `TryParse` → `FindByPrefixAsync` → `hasher.Matches` → usable at `clock.UtcNow` → `PartnerActive`. Any failure returns `null`, and the reason is logged at Information as one of `malformed|unknown_prefix|bad_secret|revoked|expired|suspended` together with the **prefix only** (only after a successful parse). The Installer binds `ApiKeyOptions` from `ApiKeys`, registers `ApiKeyPepper.FromConfiguration(config["ApiKeys:Pepper"])` as a singleton, and registers `ApiKeyHasher`. Migration as listed.
- [ ] **Step 4: Run** the Step 2 command, then `dotnet build EwaysVNext.slnx`. Expected: PASS.
- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Partners src/Database src/Host tests/UnitTests/Partners
git commit -m "feat(partners): admin service, key authenticator, persistence and migration" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 6: `PartnerApiKey` scheme, scope policies, error bodies and per-partner rate limit

**Files:**
- Create: `src/Modules/Partners/Adapter/Inbound/Rest/Authentication/PartnerApiKeyAuthenticationHandler.cs`; modify `Adapter/Inbound/Rest/DependencyInjection.cs` (`AddPartnersRestAdapter` registers the scheme with `services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, PartnerApiKeyAuthenticationHandler>(AuthSchemes.PartnerApiKey, _ => { })`)
- Create: `src/BuildingBlocks/Presentation.Endpoint/Security/PartnerScopeRequirement.cs`, `EwaysAuthorizationResultHandler.cs`; modify `AuthorizationPolicyRegistration.cs`
- Modify: `src/Host/EwaysVNext.Host/Authentication/Extensions/KeycloakAuthenticationExtensions.cs` (`SelectTenantScheme`)
- Create: `src/Host/EwaysVNext.Host/RateLimiting/RateLimitingSetup.cs`; modify `Program.cs` (replace inline `AddRateLimiter`; order `UseAuthentication()` → `UseRateLimiter()` → `UseAuthorization()`; register `EwaysAuthorizationResultHandler` as `IAuthorizationMiddlewareResultHandler` and `PartnerScopeAuthorizationHandler` as `IAuthorizationHandler`)
- Modify: `tests/IntegrationTests/PostgresFixture.cs` (`CreateFactory(Action<IServiceCollection>? configureServices = null, IDictionary<string,string?>? settings = null)`; the test auth default becomes a policy scheme `IntegrationTestRouter` that forwards to `AuthSchemes.PartnerApiKey` when `AuthSchemes.IsPartnerKey(request)` and otherwise to the test JWT scheme; settings `ApiKeys:Pepper` = 40-char test value and `RateLimiting:AnonymousPermitPerMinute = 100000`)
- Create: `tests/IntegrationTests/Support/{MutableClock,PartnerSeeder,BearerClientExtensions}.cs`, `tests/IntegrationTests/Partners/PartnerProbeEndpoints.cs`
- Test: `tests/IntegrationTests/Partners/PartnerApiKeyAuthenticationTests.cs`

**Interfaces:**
```csharp
public sealed class PartnerApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> o, ILoggerFactory l, UrlEncoder e,
    IApiKeyAuthenticator authenticator) : AuthenticationHandler<AuthenticationSchemeOptions>(o, l, e)
// NoResult when the bearer is not "pk_…"; Fail("Invalid API key.") on null; never logs or echoes the presented value.
// Claims: partner_id, api_key_id, scope (one claim per scope), rate_limit_per_minute, bulk_max_items; AuthenticationType "PartnerApiKey".

public sealed record PartnerScopeRequirement(string Scope) : IAuthorizationRequirement;
public sealed class PartnerScopeAuthorizationHandler : AuthorizationHandler<PartnerScopeRequirement>; // fails with reason message "INSUFFICIENT_SCOPE"
// policy "partner:{scope}" for each of PartnerScopes.All: AddAuthenticationSchemes(AuthSchemes.PartnerApiKey), RequireAuthenticatedUser, PartnerScopeRequirement(scope)
public sealed class EwaysAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler;
// Challenged → 401 body Error.Unauthorized + "WWW-Authenticate: Bearer"; Forbidden with reason INSUFFICIENT_SCOPE →
// 403 Error("INSUFFICIENT_SCOPE", "The API key does not grant the scope this endpoint requires.", 403); other Forbidden → 403 Error.Forbidden;
// bodies written via Result.Failure(error).ToHttpResult().ExecuteAsync(httpContext). Succeeded → next.
public static class RateLimitingSetup { public static IServiceCollection AddEwaysRateLimiting(this IServiceCollection s, IConfiguration c); }
// Partition: PartnerPrincipal present → key "partner:{id}:{limit}", FixedWindow(PermitLimit = rate_limit_per_minute claim, 1 min, QueueLimit 0);
// else key = remote IP, PermitLimit = RateLimiting:AnonymousPermitPerMinute (default 100). Putting the limit in the key makes an admin
// change apply immediately. OnRejected: 429, "Retry-After: 60", body Error("RATE_LIMITED", …, 429, Retryable: true).
// Keeps the existing named "admin" policy unchanged.
```
`SelectTenantScheme`: after extracting the token, `if (token.StartsWith(AuthSchemes.PartnerKeyPrefix, StringComparison.Ordinal)) return AuthSchemes.PartnerApiKey;`
runs **before** `JwtSecurityTokenHandler.CanReadToken`, so a partner key is never parsed as a JWT
or logged by the JWT path.

Test helpers: `MutableClock : IClock` (`UtcNow`, `Advance(TimeSpan)`), registered in place of
`IClock`. `PartnerSeeder.CreateAsync(factory, scopes, rateLimit = 300)` →
`(long PartnerId, long KeyId, string Key)`, which resolves `IPartnerAdminService` in a scope with
`new AdminActor(Guid.NewGuid(), true)`. It also has `SuspendAsync`, `RevokeAsync` and `RotateAsync(overlap)`.
`PartnerProbeEndpoints : IEndpoint` maps `GET /test/partner/{scope-slug}` for each scope, with
`RequireAuthorization(AuthPolicies.Partner(scope))`, returning `Result.Success(new { partnerId }).ToHttpResult()`.
It is registered in the factory with `services.AddScoped<IEndpoint, PartnerProbeEndpoints>()`.

- [ ] **Step 1: Write the failing tests** (collection = phase 0's Postgres collection).
```csharp
[Fact] public async Task Valid_Key_With_Scope_Returns_200() { /* scopes [orders:create] → GET /test/partner/orders-create 200 */ }
[Fact] public async Task Wrong_Scope_Returns_403_Insufficient_Scope()
{
    var (_, _, key) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersRead]);
    var response = await client.GetWithBearerAsync("/test/partner/orders-create", key);
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
    body.GetProperty("code").GetString().Should().Be("INSUFFICIENT_SCOPE");
    body.GetProperty("retryable").GetBoolean().Should().BeFalse();
}
[Fact] public async Task Suspended_Partner_Key_Returns_401() { /* 200, suspend, same key → 401, code UNAUTHORIZED */ }
[Fact] public async Task Revoked_Key_Returns_401() { /* 200, revoke, → 401 */ }
[Fact] public async Task Rotated_Old_Key_Works_Until_Overlap_Expiry()
{
    var (pid, kid, oldKey) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersCreate]);
    var newKey = await PartnerSeeder.RotateAsync(factory, pid, kid, TimeSpan.FromHours(1));
    (await Probe(oldKey)).Should().Be(HttpStatusCode.OK);
    clock.Advance(TimeSpan.FromHours(1) - TimeSpan.FromSeconds(1));
    (await Probe(oldKey)).Should().Be(HttpStatusCode.OK);
    clock.Advance(TimeSpan.FromSeconds(1));
    (await Probe(oldKey)).Should().Be(HttpStatusCode.Unauthorized);
    (await Probe(newKey)).Should().Be(HttpStatusCode.OK);
}
[Fact] public async Task Malformed_Partner_Key_Returns_401() { /* "pk_garbage" → 401 */ }
[Fact] public async Task Backoffice_Jwt_On_Partner_Endpoint_Returns_401() { /* super admin JWT → 401: partner policies authenticate only with PartnerApiKey */ }
[Fact] public async Task Partner_Rate_Limit_Returns_429_Rate_Limited()
{ /* rateLimit 2: requests 1–2 → 200, 3 → 429, code RATE_LIMITED, retryable true, Retry-After present */ }
```
- [ ] **Step 2: Run** `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter "FullyQualifiedName~PartnerApiKeyAuthenticationTests"`. Expected: FAIL (scheme not registered, so `pk_` goes to the JWT path → 401 everywhere and the 200 cases fail).
- [ ] **Step 3: Implement** as specified above.
- [ ] **Step 4: Run** the Step 2 command, then the whole integration suite (the rate-limiter and middleware-order change touches every request). Expected: PASS.
- [ ] **Step 5: Mutation check.** Make `IsUsableAt` use `<=` → `Rotated_Old_Key_Works_Until_Overlap_Expiry` fails. Drop the `INSUFFICIENT_SCOPE` reason → `Wrong_Scope_Returns_403_Insufficient_Scope` fails. Restore both.
- [ ] **Step 6: Commit.**
```bash
git add src tests/IntegrationTests
git commit -m "feat(partners): PartnerApiKey auth scheme, partner:{scope} policies, per-partner rate limit" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 7: Usage log (bounded channel + batch insert), UsagePrune job, key never logged

**Files:**
- Create: `src/Modules/Partners/Application/Usage/{ApiKeyUsage,ApiKeyUsageEntry}.cs`, `Application/Partners/Ports/Inbound/IApiKeyUsageRecorder.cs`
- Create: `src/Modules/Partners/Adapter/Outbound/Usage/{ChannelApiKeyUsageRecorder,ApiKeyUsageFlushService,UsagePruneJobHandler,PartnerUsageOptions}.cs`
- Create: `src/Modules/Partners/Adapter/Inbound/Rest/Usage/PartnerApiUsageMiddleware.cs`
- Create: `src/Database/Partners/Configurations/ApiKeyUsageConfiguration.cs`; `DbSet<ApiKeyUsage> ApiKeyUsages`; migration `dotnet ef migrations add PartnerUsage -p src/Database -s src/Host/EwaysVNext.Host`
- Modify: `Installer/ModuleInstaller.cs` (`UsePartnersModule(this IApplicationBuilder app)` → `UseMiddleware<PartnerApiUsageMiddleware>()`; `AddJobHandler<UsagePruneJobHandler, UsagePrunePayload>()`; `AddRecurringJob(JobTypes.UsagePrune, TimeSpan.FromDays(1))`; `AddHostedService<ApiKeyUsageFlushService>()`)
- Modify: `Program.cs` (`app.UsePartnersModule()` between `UseAuthentication()` and `UseRateLimiter()`, so 429s are logged too; Serilog: `UseSerilog((ctx, services, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration).ReadFrom.Services(services).WriteTo.Console(…))`)
- Create: `tests/IntegrationTests/Support/CapturingSink.cs`
- Test: `tests/UnitTests/Partners/ChannelApiKeyUsageRecorderTests.cs`, `tests/IntegrationTests/Partners/PartnerUsageTests.cs`

**Interfaces:**
```csharp
public sealed class ApiKeyUsage { long Id; long PartnerId; long KeyId; DateTimeOffset At; string Method; string Path; int Status; string CorrelationId; int DurationMs; } // partners.api_key_usage
public sealed record ApiKeyUsageEntry(long PartnerId, long KeyId, DateTimeOffset At, string Method, string Path, int Status, string CorrelationId, int DurationMs);
public interface IApiKeyUsageRecorder { bool TryRecord(ApiKeyUsageEntry entry); } // never blocks; false = dropped (counter "partner_api_usage_dropped")
public sealed class PartnerUsageOptions { int ChannelCapacity = 10_000; int BatchSize = 500; TimeSpan FlushInterval = 1s; TimeSpan Retention = 90d; } // section "Partners:Usage"
public sealed record UsagePrunePayload;
public sealed class UsagePruneJobHandler : IJobHandler<UsagePrunePayload> { Type => JobTypes.UsagePrune; } // ExecuteDeleteAsync(At < now - Retention) → JobOutcome.Completed
```
Channel: `Channel.CreateBounded<ApiKeyUsageEntry>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true })`.
`TryRecord` is `writer.TryWrite`. The flush service reads up to `BatchSize` entries or waits
`FlushInterval`. It then opens a scope, assigns `IIdGenerator` ids, calls `AddRange` and one
`SaveChangesAsync`. A failed batch is logged (count only) and dropped; the loop never throws out.
The middleware runs `next()` in try/finally and records only when
`PartnerPrincipal.TryFrom(context.User, …)` succeeds. `Path` is `Request.Path`, truncated to 512
characters; the query string is never included. The correlation id is the response
`X-Correlation-Id`. Indexes: `(at)`, `(partner_id, at DESC)`.

- [ ] **Step 1: Write the failing tests.**
```csharp
// unit
[Fact] public void TryRecord_Returns_False_Instead_Of_Blocking_When_Full()
{
    var sut = new ChannelApiKeyUsageRecorder(Options.Create(new PartnerUsageOptions { ChannelCapacity = 2 }));
    sut.TryRecord(Entry()).Should().BeTrue(); sut.TryRecord(Entry()).Should().BeTrue();
    sut.TryRecord(Entry()).Should().BeFalse();
}
// integration
[Fact] public async Task Partner_Requests_Are_Recorded_In_The_Usage_Log()
{ /* 200 + 403 probe calls → within 5 s (poll every 100 ms) two rows for the partner with status 200 and 403, path "/test/partner/…", non-empty correlation id */ }
[Fact] public async Task UsagePrune_Deletes_Rows_Older_Than_90_Days()
{ /* insert rows at now-91d and now-89d; run the handler from DI; only the now-89d row remains */ }
[Fact] public async Task Api_Key_Never_Appears_In_Logs()
{
    var sink = new CapturingSink();
    using var factory = fixture.CreateFactory(s => s.AddSingleton<ILogEventSink>(sink),
        new Dictionary<string, string?> { ["Serilog:MinimumLevel:Default"] = "Verbose", ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose" });
    var (pid, kid, key) = await PartnerSeeder.CreateAsync(factory, [PartnerScopes.OrdersCreate]);
    var secret = key.Split('_')[2];
    await client.GetWithBearerAsync("/test/partner/orders-create", key);          // 200
    await client.GetWithBearerAsync("/test/partner/customers-read", key);         // 403
    await client.GetWithBearerAsync("/test/partner/orders-create", key[..^1] + (key[^1] == 'A' ? 'B' : 'A')); // 401 bad secret
    await PartnerSeeder.RevokeAsync(factory, pid, kid);
    await client.GetWithBearerAsync("/test/partner/orders-create", key);          // 401 revoked
    sink.Events.Should().NotBeEmpty();
    sink.AllText().Should().NotContain(secret).And.NotContain(key);   // rendered message + every property value + exception text
}
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~ChannelApiKeyUsageRecorderTests"` and `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter "FullyQualifiedName~PartnerUsageTests"`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement** as specified above.
- [ ] **Step 4: Run** both commands. Expected: PASS.
- [ ] **Step 5: Mutation check.** Add `Logger.LogInformation("auth {Key}", presented)` in the handler → `Api_Key_Never_Appears_In_Logs` fails. Restore.
- [ ] **Step 6: Commit.**
```bash
git add src tests
git commit -m "feat(partners): non-blocking API key usage log with batch insert and 90-day prune" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 8: Partners admin REST and GraphQL

**Files:**
- Create: `src/Modules/Partners/QueryApplication/{IPartnerAdminQueries,PartnerAdminQueries,PartnerAdminShapes,DependencyInjection}.cs`
- Create: `src/Modules/Partners/Adapter/Inbound/Rest/Admin/{PartnerAdminEndpoints,PartnerAdminRequests}.cs`
- Create: `src/Modules/Partners/Adapter/Inbound/GraphQL/{PartnerAdminQueries,PartnerAdminMutations,PartnerGraphQLTypes,DependencyInjection}.cs`
- Modify: `Installer/ModuleInstaller.cs`
- Test: `tests/IntegrationTests/Partners/PartnerAdminApiTests.cs`

**Interfaces:**
```csharp
public sealed record PartnerListItem([Snowflake] long Id, string Name, string Status, IReadOnlyList<string> Scopes, int RateLimitPerMinute, int BulkMaxItems, int ActiveKeyCount, DateTimeOffset CreatedAt);
public sealed record ApiKeyView([Snowflake] long Id, string Prefix, IReadOnlyList<string> Scopes, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt,
  DateTimeOffset? RevokedAt, string Status /* Active|Expired|Revoked at now */, DateTimeOffset? LastUsedAt /* max(api_key_usage.at) */);
public sealed record PartnerDetail(PartnerListItem Partner, IReadOnlyList<ApiKeyView> Keys);
public sealed record ApiKeyUsageItem([Snowflake] long KeyId, DateTimeOffset At, string Method, string Path, int Status, string CorrelationId, int DurationMs);
public interface IPartnerAdminQueries {
  Task<PaginatedQuery<PartnerListItem>> ListAsync(int page, int pageSize, CancellationToken ct = default);           // pageSize 1–100
  Task<Result<PartnerDetail>> GetAsync(long partnerId, DateTimeOffset now, CancellationToken ct = default);
  Task<PaginatedQuery<ApiKeyUsageItem>> GetUsageAsync(long partnerId, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, CancellationToken ct = default); // newest first
}
```
REST: the group `/api/v1/admin/partners` has `.RequireAuthorization(AuthPolicies.AdminSuper).RequirePermission(AdminPermissions.PartnersManage)`.
Every handler builds the actor with `AdminActor.TryFrom(user, out var actor)` and returns
`EndpointResult.Forbidden()` when that fails.

| Method & path | Port call | Response |
|---|---|---|
| `POST /` body `{name, scopes, rateLimitPerMinute?, bulkMaxItems?}` | `CreateAsync` | 201 `{id}` |
| `GET /?page&pageSize` | `ListAsync` | 200 |
| `GET /{id}` | `GetAsync` | 200 / 404 |
| `POST /{id}/suspend`, `POST /{id}/reactivate` | `SuspendAsync` / `ReactivateAsync` | 200 |
| `PUT /{id}/scopes` body `{scopes}` | `SetScopesAsync` | 200 |
| `PUT /{id}/limits` body `{rateLimitPerMinute, bulkMaxItems}` | `SetLimitsAsync` | 200 |
| `POST /{id}/keys` body `{scopes?, expiresAt?}` | `IssueKeyAsync` | 201 `IssuedApiKey`, header `Cache-Control: no-store` |
| `POST /{id}/keys/{keyId}/rotate` body `{overlapHours?}` | `RotateKeyAsync` | 201 `IssuedApiKey`, `no-store` |
| `POST /{id}/keys/{keyId}/revoke` | `RevokeKeyAsync` | 200 |
| `GET /{id}/usage?from&to&page&pageSize` | `GetUsageAsync` | 200 |

GraphQL (each field has `[Authorize(Policy = AuthPolicies.AdminSuper)]` and `[RequirePermission(AdminPermissions.PartnersManage)]`):
queries `adminPartners(page, pageSize)`, `adminPartner(id)`, `adminPartnerUsage(id, from, to, page, pageSize)`;
mutations `createPartner`, `suspendPartner`, `reactivatePartner`, `setPartnerScopes`, `setPartnerLimits`,
`issuePartnerApiKey`, `rotatePartnerApiKey`, `revokePartnerApiKey`. The issue and rotate mutations
return `IssuedApiKeyPayload { keyId, prefix, key, scopes, expiresAt }`.

- [ ] **Step 1: Write the failing tests** (super admin token unless stated).
```csharp
[Fact] public async Task Super_Admin_Creates_Partner_And_Issues_A_Working_Key()
{ /* POST partner → POST keys → 201, body.key matches ^pk_…$, Cache-Control no-store; probe with key → 200 */ }
[Fact] public async Task Plaintext_Key_Is_Returned_Once_And_Never_By_Reads()
{ /* GET /{id} and adminPartner(id) JSON contain body.prefix but not body.key nor its secret part */ }
[Fact] public async Task Partner_Detail_Shows_Rotation_State()
{ /* rotate with overlapHours 1 → old key Status Active with ExpiresAt; new key Active; after revoke → Revoked */ }
[Fact] public async Task Usage_Lists_Recorded_Requests_Newest_First() { }
[Fact] public async Task GraphQL_IssuePartnerApiKey_Returns_The_Key_Once() { }
[Fact] public async Task Unknown_Partner_Returns_404() { }
```
- [ ] **Step 2: Run** `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter "FullyQualifiedName~PartnerAdminApiTests"`. Expected: FAIL (404 route not found).
- [ ] **Step 3: Implement.** `LastUsedAt` is a correlated `Max(u => u.At)` over `ApiKeyUsages` in the same projection.
- [ ] **Step 4: Run** the Step 2 command and `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`. Expected: PASS.
- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Partners tests/IntegrationTests/Partners
git commit -m "feat(partners): Super Admin REST and GraphQL for partners, keys and usage" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 9: Settings module — domain, seeded defaults, `ISettingsPort`

**Files:**
- Run: `./New-EwaysModule.ps1 -ModuleName Settings`; add the GraphQL adapter csproj as in Task 3; reference Settings.Application from `src/Database/EwaysVNext.Database.csproj`; `ModuleNames` += `"Settings"`; ArchitectureTests and UnitTests csproj references
- Create: `src/Modules/Settings/Application/Settings/Domain/{SellerProfile,PaymentInstruction,PaymentInstructionVersion,CommerceLimits,BuyerFieldRules,SettingsDefaults}.cs`
- Create: `src/Modules/Settings/Application/Settings/Ports/Inbound/ISettingsPort.cs`, `Ports/Outbound/ISettingsRepository.cs`, `Services/{SettingsPortService,FxAgeOptions}.cs`, `Application/DependencyInjection.cs`
- Create: `src/Modules/Settings/Adapter/Outbound/Persistence/SettingsRepository.cs`, `Adapter/Outbound/DependencyInjection.cs`, `Installer/ModuleInstaller.cs` (`AddSettingsModule(IConfiguration)`)
- Create: `src/Database/Settings/Configurations/{SellerProfileConfiguration,PaymentInstructionConfiguration,CommerceLimitsConfiguration}.cs`; three `DbSet`s; migration `dotnet ef migrations add Settings -p src/Database -s src/Host/EwaysVNext.Host`
- Modify: `Program.cs` (`AddSettingsModule`)
- Test: `tests/UnitTests/Settings/SettingsDomainTests.cs`, `tests/IntegrationTests/Settings/SettingsSeedTests.cs`

**Interfaces** (contract names exact):
```csharp
public interface ISettingsPort {
  Task<SellerSnapshot> GetSellerAsync(CancellationToken ct = default);
  Task<PaymentInstructionSnapshot> GetActivePaymentInstructionAsync(CancellationToken ct = default);
  Task<CommerceLimitsSnapshot> GetCommerceLimitsAsync(CancellationToken ct = default);
}
public sealed record SellerSnapshot(string LegalName, string Uen, string Address, string Email, bool GstRegistered, string InvoiceFooter, IReadOnlyList<string> RequiredBuyerFields);
public sealed record PaymentInstructionSnapshot(long Id, int Version, string Text);   // Id = the version row id
public sealed record CommerceLimitsSnapshot(long MinUsdMinor, long MaxUsdMinor, int BulkDefaultMax, int CompositionDeadlineMs, int CompositionMaxLines, TimeSpan FxMaxRateAge);

public static class SettingsDefaults {
  public const long SingletonId = 1;
  MinUsdMinor = 1_000; MaxUsdMinor = 100_000_000; BulkDefaultMax = 100; CompositionDeadlineMs = 8000; CompositionMaxLines = 200;
  RequiredBuyerFields = ["email|phone"]; InitialPaymentInstructionText = "Pay by bank transfer using the details on your pro forma invoice. Quote the pro forma number as the payment reference.";
  // seller placeholders, runbook says "configure before go-live": LegalName "Configure seller legal name", Uen "UNSET", Address "Singapore", Email "billing@example.invalid", InvoiceFooter ""
}
public static class BuyerFieldRules {   // each entry = alternatives joined by '|'; an entry is satisfied when any alternative is present
  public static readonly IReadOnlySet<string> Fields = {"email","phone","fullName","companyName","billingAddress","registrationNumber"};
  public static void Validate(IReadOnlyList<string> rules); // non-empty entries, known fields only → else DomainException VALIDATION_FAILED
}
public sealed class SellerProfile : AggregateRoot<long> { … void Update(string legalName, string uen, string address, string email, bool gstRegistered, string invoiceFooter, IReadOnlyList<string> requiredBuyerFields); }
// gstRegistered == true → DomainException VALIDATION_FAILED ("GST registration is not supported; ITaxPolicy returns zero tax")
public sealed class PaymentInstruction : AggregateRoot<long> {   // ONE aggregate, so "exactly one active version" is a single-save invariant
  public int ActiveVersion; public IReadOnlyList<PaymentInstructionVersion> Versions;
  public PaymentInstructionVersion Publish(long versionId, string text, Guid createdBy, DateTimeOffset now); // appends Version = max+1 and activates it
  public void Activate(int version);                                     // must exist → NOT_FOUND
  public PaymentInstructionVersion Active { get; }
}
public sealed class PaymentInstructionVersion : Entity<long> { int Version; string Text; Guid? CreatedBy; DateTimeOffset CreatedAt; } // immutable; Text 1–4000 chars
public sealed class CommerceLimits : AggregateRoot<long> { … void Update(long minUsdMinor, long maxUsdMinor, int bulkDefaultMax, int compositionDeadlineMs, int compositionMaxLines); }
// 0 < min < max; bulk 1–1000; deadline 1000–9500 ms (stays inside the 10 s p95); maxLines 1–1000
public sealed class FxAgeOptions { public TimeSpan MaxRateAge { get; init; } = TimeSpan.FromHours(72); } // bound from "Fx" section (Fx:MaxRateAge)
```
`CommerceLimitsSnapshot.FxMaxRateAge` comes from `Fx:MaxRateAge` config (the same key Pricing
reads), not from a stored column. See Spec conflicts. **Persistence:** all three tables have
`CHECK (id = 1)`. `payment_instruction_versions` has `UNIQUE(payment_instruction_id, version)`.
`required_buyer_fields text[]`. The seed is `HasData` with id `1` (singletons; Snowflake ids are far
larger, so there is no collision), and version row id `1` holds `Version = 1`.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public void Seller_Refuses_Gst_Registration() { /* Update(..., gstRegistered: true, ...) → VALIDATION_FAILED */ }
[Theory, InlineData("email|fax"), InlineData(""), InlineData("email||phone")]
public void Unknown_Or_Empty_Buyer_Field_Rules_Are_Refused(string rule) { }
[Fact] public void Publish_Appends_Next_Version_And_Activates_It()
{ /* v1 active; Publish("new") → Versions [1,2], ActiveVersion 2, v1 text unchanged */ }
[Fact] public void Activate_Unknown_Version_Is_Not_Found() { }
[Theory, InlineData(1000, 1000), InlineData(0, 10), InlineData(1000, 100_000_000, 0), InlineData(1000, 100_000_000, 100, 9501)]
public void CommerceLimits_Bounds_Are_Enforced(long min, long max, int bulk = 100, int deadline = 8000) { }

// integration: migrations applied by PostgresFixture
[Fact] public async Task Seeded_Defaults_Match_Spec()
{
    var port = Scope.GetRequiredService<ISettingsPort>();
    (await port.GetCommerceLimitsAsync()).Should().Be(new CommerceLimitsSnapshot(1_000, 100_000_000, 100, 8000, 200, TimeSpan.FromHours(72)));
    var seller = await port.GetSellerAsync();
    seller.GstRegistered.Should().BeFalse(); seller.RequiredBuyerFields.Should().Equal("email|phone");
    (await port.GetActivePaymentInstructionAsync()).Version.Should().Be(1);
}
[Fact] public async Task Second_Singleton_Row_Is_Rejected_By_Check_Constraint() { /* raw INSERT id=2 → PostgresException 23514 */ }
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~Settings"` and `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter "FullyQualifiedName~SettingsSeedTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** as specified above. `SettingsPortService` is `sealed`, reads through `ISettingsRepository` (no tracking), and maps to snapshots.
- [ ] **Step 4: Run** both commands. Expected: PASS.
- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Settings src/Database src/Host EwaysVNext.slnx tests
git commit -m "feat(settings): seller profile, versioned payment instruction, commerce limits, ISettingsPort" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 10: Settings admin service, REST and GraphQL (Super Admin)

**Files:**
- Create: `src/Modules/Settings/Application/Settings/Ports/Inbound/{ISettingsAdminService,SettingsCommands}.cs`, `Services/SettingsAdminService.cs`
- Create: `src/Modules/Settings/QueryApplication/{ISettingsAdminQueries,SettingsAdminQueries,SettingsShapes,DependencyInjection}.cs`
- Create: `src/Modules/Settings/Adapter/Inbound/Rest/Admin/SettingsAdminEndpoints.cs`, `Adapter/Inbound/GraphQL/{SettingsAdminQueries,SettingsAdminMutations,DependencyInjection}.cs`
- Test: `tests/UnitTests/Settings/SettingsAdminServiceTests.cs`, `tests/IntegrationTests/Settings/SettingsAdminApiTests.cs`

**Interfaces:**
```csharp
public sealed record UpdateSellerCommand(string LegalName, string Uen, string Address, string Email, bool GstRegistered, string InvoiceFooter, IReadOnlyList<string> RequiredBuyerFields);
public sealed record UpdateCommerceLimitsCommand(long MinUsdMinor, long MaxUsdMinor, int BulkDefaultMax, int CompositionDeadlineMs, int CompositionMaxLines);
public interface ISettingsAdminService {   // Super Admin only; audited ("settings.seller_updated", "settings.payment_instruction_published|activated", "settings.limits_updated")
  Task<Result> UpdateSellerAsync(UpdateSellerCommand command, AdminActor actor, CancellationToken ct = default);
  Task<Result<int>> PublishPaymentInstructionAsync(string text, AdminActor actor, CancellationToken ct = default);   // returns the new version
  Task<Result> ActivatePaymentInstructionAsync(int version, AdminActor actor, CancellationToken ct = default);
  Task<Result> UpdateCommerceLimitsAsync(UpdateCommerceLimitsCommand command, AdminActor actor, CancellationToken ct = default);
}
public interface ISettingsAdminQueries { Task<SellerView> GetSellerAsync(…); Task<IReadOnlyList<PaymentInstructionVersionView>> GetPaymentInstructionsAsync(…); Task<CommerceLimitsView> GetCommerceLimitsAsync(…); }
```
REST: the group `/api/v1/admin/settings` has `.RequireAuthorization(AuthPolicies.AdminSuper).RequirePermission(AdminPermissions.SettingsManage)`.
Routes: `GET|PUT /seller`, `GET /payment-instructions`, `POST /payment-instructions` `{text}` → 201
`{version}`, `POST /payment-instructions/{version:int}/activate`, `GET|PUT /commerce-limits`.
The money fields on the wire are strings `"10.00"` (`MinUsd`, `MaxUsd`), parsed with
`MinorUnits.ToMinor`; the format is `^\d+\.\d{2}$`, otherwise `VALIDATION_FAILED`.
GraphQL: `adminSettings { seller paymentInstructions commerceLimits }`; mutations `updateSeller`,
`publishPaymentInstruction`, `activatePaymentInstruction`, `updateCommerceLimits`, with the same
`[Authorize(Policy = AuthPolicies.AdminSuper)]` + `[RequirePermission(AdminPermissions.SettingsManage)]`.

- [ ] **Step 1: Write the failing tests.**
```csharp
// unit
[Fact] public async Task Staff_Actor_Is_Forbidden() { /* every method → 403, repository untouched */ }
[Fact] public async Task Publish_Records_Audit_With_Version_And_Text() { }
// integration (super admin)
[Fact] public async Task Published_Instruction_Becomes_The_Active_Snapshot()
{ /* POST {text:"Wire to DBS 123"} → 201 {version:2}; ISettingsPort.GetActivePaymentInstructionAsync → (Version 2, "Wire to DBS 123") */ }
[Fact] public async Task Reactivating_An_Old_Version_Restores_Its_Text_Unchanged() { }
[Fact] public async Task Commerce_Limits_Round_Trip_As_Two_Decimal_Strings() { /* PUT minUsd "25.00" → GET "25.00"; port MinUsdMinor 2500 */ }
[Theory, InlineData("25"), InlineData("25.0"), InlineData("25.001"), InlineData("-1.00")]
public async Task Malformed_Usd_Amount_Is_400_Validation_Failed(string minUsd) { }
[Fact] public async Task Gst_Registered_True_Is_Refused() { /* 400 VALIDATION_FAILED */ }
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~SettingsAdminServiceTests"` and `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter "FullyQualifiedName~SettingsAdminApiTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** as specified above (load singleton → mutate → audit → one save).
- [ ] **Step 4: Run** both commands. Expected: PASS.
- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Settings tests
git commit -m "feat(settings): Super Admin REST and GraphQL for seller, payment instruction and limits" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 11: Customer domain — `Customer`, `ContactChange`, `Phone`, `Address`

**Files:**
- Create: `src/Modules/Customer/Application/Customers/Domain/{Customer,CustomerKind,CustomerStatus,ContactChange,ContactField,Phone,Address,ContactUpdate,ProfileUpdate}.cs`
- Test: `tests/UnitTests/Customers/PhoneTests.cs`, `AddressTests.cs`, `CustomerDomainTests.cs` (delete any stale Customer unit tests phase 0 left behind that no longer compile)

**Interfaces:**
```csharp
public enum CustomerKind { Website, PartnerLinked }   public enum CustomerStatus { Active, Suspended }   public enum ContactField { Email, Phone, FullName }
public sealed class Phone : ValueObject { public string Value; public static bool TryCreate(string? raw, out Phone phone); }
// strips spaces, '-', '(', ')', '.'; result must match ^\+[1-9]\d{6,14}$ (E.164)
public sealed class Address : ValueObject { Line1; Line2?; City; PostalCode; CountryIso2;
  public static Address Create(string line1, string? line2, string city, string postalCode, string countryIso2); } // trims; uppercases ISO-2; ^[A-Z]{2}$; lengths 200/200/100/20
public sealed record ContactUpdate(Email? Email, Phone? Phone, string? FullName);   // null = omitted = keep
public sealed record ProfileUpdate(string? FullName, string? CompanyName, Phone? Phone, Address? BillingAddress, string? RegistrationNumber);
public sealed class ContactChange : Entity<long> { ContactField Field; string? OldValue; string? NewValue; DateTimeOffset ChangedAt; long? SourcePartnerId; string CorrelationId; }
public sealed class Customer : AggregateRoot<long> {
  CustomerKind Kind; Guid? IdentityUserId; long? PartnerId; string? ExternalCustomerId; string? Email; string? Phone; bool EmailVerified;
  string? FullName; string? CompanyName; Address? BillingAddress; string? RegistrationNumber; CustomerStatus Status; DateTimeOffset CreatedAt;
  IReadOnlyList<ContactChange> ContactChanges;
  public static Customer CreateWebsite(long id, Guid identityUserId, Email email, bool emailVerified, DateTimeOffset now);
  public static Customer CreatePartnerLinked(long id, long partnerId, string externalCustomerId, Email? email, Phone? phone, string? fullName, DateTimeOffset now);
    // email or phone null → DomainException CUSTOMER_CONTACT_REQUIRED; externalCustomerId trimmed, 1–100 chars
  public bool SyncPartnerContact(ContactUpdate update, long sourcePartnerId, string correlationId, Func<long> nextId, DateTimeOffset now); // true when anything changed
  public void MirrorIdentity(Email email, bool emailVerified, string correlationId, Func<long> nextId, DateTimeOffset now);           // Website only
  public void UpdateProfile(ProfileUpdate update, string correlationId, Func<long> nextId, DateTimeOffset now);                        // Website only; Active only
  public void Suspend(); public void Reactivate();
}
```
Rules: a changed Email, Phone or FullName appends one `ContactChange` (old, new, `now`,
`SourcePartnerId`, `correlationId`). An equal value appends nothing. Sync never sets
`EmailVerified` to true; a changed email on a partner-linked customer stays `false`.
`MirrorIdentity` takes `EmailVerified` from Keycloak, the authority for website accounts.
Calling a Website-only method on a PartnerLinked customer, or the reverse, throws
`DomainException("VALIDATION_FAILED", …)`. `UpdateProfile` on a suspended customer throws
`CUSTOMER_SUSPENDED` (→ 403 via `Error.SuggestHttpStatus`).

- [ ] **Step 1: Write the failing tests.**
```csharp
[Theory, InlineData("+65 9123 4567", "+6591234567"), InlineData("+1 (415) 555-0100", "+14155550100")]
public void Phone_Normalizes_To_E164(string raw, string expected) { Phone.TryCreate(raw, out var p).Should().BeTrue(); p.Value.Should().Be(expected); }
[Theory, InlineData("91234567"), InlineData("+0123456789"), InlineData("+12345"), InlineData("+1234567890123456"), InlineData("+65abc")]
public void Phone_Rejects_Non_E164(string raw) => Phone.TryCreate(raw, out _).Should().BeFalse();
[Fact] public void Address_Uppercases_Country_And_Rejects_Three_Letters() { /* "sg" → "SG"; "SGP" → VALIDATION_FAILED */ }
[Theory, InlineData(null, "+6591234567"), InlineData("m@x.com", null)]
public void PartnerLinked_Requires_Email_And_Phone(string? email, string? phone)
    => Invoking(() => Customer.CreatePartnerLinked(1, 7, "456", E(email), P(phone), null, Now)).Should().Throw<DomainException>().Which.Code.Should().Be("CUSTOMER_CONTACT_REQUIRED");
[Fact] public void Sync_With_Omitted_Fields_Keeps_Stored_Values()
{
    var c = Customer.CreatePartnerLinked(1, 7, "456", E("a@x.com"), P("+6591234567"), "Martin", Now);
    c.SyncPartnerContact(new ContactUpdate(null, null, null), 7, "corr-1", Ids(), Now).Should().BeFalse();
    (c.Email, c.Phone, c.FullName).Should().Be(("a@x.com", "+6591234567", "Martin"));
    c.ContactChanges.Should().BeEmpty();
}
[Fact] public void Sync_Appends_One_Change_Per_Changed_Field_With_Source_And_Correlation()
{ /* new email + same phone + new name → 2 changes: (Email,a@x.com,b@x.com,7,"corr-2"), (FullName,Martin,Martin Tan,7,"corr-2"); EmailVerified false */ }
[Fact] public void Website_Mirror_Takes_Verification_From_Identity() { /* verified false → MirrorIdentity(same email, true) → EmailVerified true, no ContactChange */ }
[Fact] public void Profile_Update_On_Suspended_Customer_Is_Refused() { /* CUSTOMER_SUSPENDED */ }
[Fact] public void Website_Only_Methods_Refuse_PartnerLinked() { }
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~Customers"`. Expected: FAIL.
- [ ] **Step 3: Implement** as specified above. Reuse `EwaysVNext.Domain.ValueObjects.Email`.
- [ ] **Step 4: Run** the Step 2 command. Expected: PASS.
- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Customer/Application tests/UnitTests/Customers
git commit -m "feat(customer): Customer aggregate with contact-change audit, E.164 phone, address" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 12: Customer persistence, unique-violation save, resolution and website ports

**Files:**
- Consumes (phase 0 — do NOT redefine): `IUnitOfWork.TrySaveChangesAsync` → `SaveOutcome { Saved, UniqueViolation, ConcurrencyConflict }`
- Create: `src/Modules/Customer/Application/Customers/Ports/Inbound/{ICustomerResolutionPort,IWebsiteCustomerPort,CustomerContracts}.cs`, `Ports/Outbound/ICustomerRepository.cs`, `Services/{CustomerResolutionService,WebsiteCustomerService}.cs`; rewrite `Application/DependencyInjection.cs`
- Create: `src/Modules/Customer/Adapter/Outbound/Persistence/CustomerRepository.cs`; rewrite `Adapter/Outbound/DependencyInjection.cs` and `Installer/ModuleInstaller.cs`
- Create/replace: `src/Database/Customer/Configurations/CustomerConfiguration.cs`; `DbSet<Customer> Customers`; migration `dotnet ef migrations add CustomerRebuild -p src/Database -s src/Host/EwaysVNext.Host`
- Test: `tests/UnitTests/Customers/CustomerResolutionServiceTests.cs`, `tests/IntegrationTests/Customers/CustomerPersistenceTests.cs`

**Interfaces** (contract names exact):
```csharp
// EwaysVNext.Persistence — produced by phase 0, consumed here:
// enum SaveOutcome { Saved, UniqueViolation, ConcurrencyConflict }; IUnitOfWork.TrySaveChangesAsync(CancellationToken ct = default)

public interface ICustomerResolutionPort {
  Task<PartnerCustomerRef?> FindPartnerCustomerAsync(long partnerId, string externalCustomerId, CancellationToken ct = default);
  Task<Result<PartnerCustomerRef>> CreatePartnerCustomerAsync(long partnerId, string externalCustomerId, ContactInput contact, string correlationId, CancellationToken ct = default);
  Task<Result> SyncPartnerContactAsync(long customerId, long partnerId, ContactInput contact, string correlationId, CancellationToken ct = default);
  Task<Result<InvoiceParty>> GetInvoicePartyAsync(long customerId, CancellationToken ct = default);
}
public sealed record ContactInput(string? Email, string? Phone, string? FullName);
public sealed record PartnerCustomerRef(long CustomerId, string? Email, string? Phone, string? FullName);
public sealed record InvoiceParty(long CustomerId, string? FullName, string? CompanyName, string? Email, string? Phone, AddressSnapshot? BillingAddress, string? RegistrationNumber);
public sealed record AddressSnapshot(string Line1, string? Line2, string City, string PostalCode, string CountryIso2);
public interface IWebsiteCustomerPort { Task<Result<WebsiteCustomerRef>> EnsureFromIdentityAsync(Guid identityUserId, string email, bool emailVerified, CancellationToken ct = default); }
public sealed record WebsiteCustomerRef(long CustomerId, bool EmailVerified, bool IsActive);

public interface ICustomerRepository {
  Task<Customer?> GetAsync(long id, CancellationToken ct = default);
  Task<Customer?> FindPartnerLinkedAsync(long partnerId, string externalCustomerId, CancellationToken ct = default);
  Task<Customer?> FindWebsiteAsync(Guid identityUserId, CancellationToken ct = default);
  void Add(Customer customer);
}
```
Behaviour:
- `CreatePartnerCustomerAsync` → `CUSTOMER_CONTACT_REQUIRED` (400) when email or phone is
  missing; `VALIDATION_FAILED` when the email or phone is malformed. On `UniqueViolation` (a
  concurrent create with the same `(partner, external id)`) it reloads and returns the existing
  ref **without syncing**. Sales calls Sync separately (spec §10.3).
- `SyncPartnerContactAsync` loads by id; a missing customer **or one with a different
  `PartnerId`** → `NOT_FOUND` (404 — cross-partner must not be inferable). On success it runs
  `SyncPartnerContact` and saves once.
- `EnsureFromIdentityAsync`: find by `sub` → `MirrorIdentity` (saves only if changed), or create.
  On `UniqueViolation` it reloads. `correlationId` for identity mirroring is the constant
  `"identity-sync"`.

**EF mapping (`customer.customers`, `customer.contact_changes`):** `kind`/`status` stored as
strings; `BillingAddress` as owned `billing_*` columns; `xmin` token; `ValueGeneratedNever`.
```csharp
b.HasIndex(c => new { c.PartnerId, c.ExternalCustomerId }).IsUnique().HasFilter("kind = 'PartnerLinked'").HasDatabaseName("ux_customers_partner_external");
b.HasIndex(c => c.IdentityUserId).IsUnique().HasFilter("kind = 'Website'").HasDatabaseName("ux_customers_identity_user");
b.ToTable(t => t.HasCheckConstraint("ck_customers_kind_keys",
  "(kind = 'Website' AND identity_user_id IS NOT NULL AND partner_id IS NULL AND external_customer_id IS NULL) OR " +
  "(kind = 'PartnerLinked' AND partner_id IS NOT NULL AND external_customer_id IS NOT NULL AND identity_user_id IS NULL)"));
// no unique index on email or phone (PRD §3: no merging)
```

- [ ] **Step 1: Write the failing tests.**
```csharp
// unit (NSubstitute repository + IUnitOfWork)
[Fact] public async Task Create_Without_Phone_Is_Customer_Contact_Required_And_Writes_Nothing()
{ /* result.Error.Code == "CUSTOMER_CONTACT_REQUIRED", HttpStatus 400; repository.DidNotReceive().Add */ }
[Fact] public async Task Sync_For_Another_Partners_Customer_Is_Not_Found() { /* customer.PartnerId 7, call with 8 → 404, no save */ }
[Fact] public async Task Create_After_Unique_Violation_Returns_The_Existing_Customer()
{ /* TrySaveChangesAsync → UniqueViolation; FindPartnerLinkedAsync second call returns existing → Success(existing ref) */ }
// integration (Postgres)
[Fact] public async Task Same_External_Customer_Id_Under_Two_Partners_Creates_Two_Customers() { }
[Fact] public async Task Duplicate_External_Id_For_One_Partner_Is_Blocked_By_The_Filtered_Index()
{ /* two raw inserts via DbContext bypassing the port → second SaveChanges throws, SqlState 23505, constraint ux_customers_partner_external */ }
[Fact] public async Task Same_Email_On_Two_Customers_Is_Allowed() { /* website + partner-linked, same email → both persisted */ }
[Fact] public async Task Concurrent_Creates_For_One_External_Id_Produce_One_Customer()
{ /* 10 parallel CreatePartnerCustomerAsync (own scopes) → all succeed, one distinct CustomerId, one row */ }
[Fact] public async Task Concurrent_First_Calls_From_One_Identity_Produce_One_Website_Customer() { /* 10 parallel EnsureFromIdentityAsync */ }
[Fact] public async Task Sync_Persists_Contact_History_With_Partner_And_Correlation() { }
[Fact] public async Task Check_Constraint_Refuses_A_Website_Row_With_A_PartnerId() { /* raw SQL → 23514 */ }
```
- [ ] **Step 2: Run** `dotnet test --project tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~CustomerResolutionServiceTests"` and `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter "FullyQualifiedName~CustomerPersistenceTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** as specified above. `GetInvoicePartyAsync` maps the aggregate (no tracking).
- [ ] **Step 4: Run** both commands. Expected: PASS.
- [ ] **Step 5: Mutation check.** Drop `.IsUnique()` from `ux_customers_partner_external` (regenerate the migration locally) → `Duplicate_External_Id_For_One_Partner…` fails. Remove the `UniqueViolation` reload → `Concurrent_Creates…` fails. Restore both and delete the scratch migration.
- [ ] **Step 6: Commit.**
```bash
git add src/BuildingBlocks/Persistence src/Modules/Customer src/Database tests
git commit -m "feat(customer): persistence with filtered uniques; resolution and website ports; TrySaveChanges" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 13: Shop `me` and `updateProfile` (REST + GraphQL, lazy creation)

**Files:**
- Create: `src/Modules/Customer/Application/Customers/Ports/Inbound/ICustomerProfileService.cs`, `Services/CustomerProfileService.cs`
- Create: `src/Modules/Customer/QueryApplication/Customers/{ICustomerShopQueries,CustomerQueries,CustomerShapes}.cs`, rewrite `QueryApplication/DependencyInjection.cs`
- Create: `src/Modules/Customer/Adapter/Inbound/Rest/Shop/{ShopIdentity,ShopMeEndpoints,ShopRequests}.cs`; rewrite `Adapter/Inbound/Rest/DependencyInjection.cs`
- Create: `src/Modules/Customer/Adapter/Inbound/GraphQL/Shop/{MeQueries,ProfileMutations}.cs`, `CustomerGraphQLTypes.cs`; rewrite `Adapter/Inbound/GraphQL/DependencyInjection.cs`
- Test: `tests/IntegrationTests/Customers/ShopProfileTests.cs`

**Interfaces:**
```csharp
public sealed record UpdateProfileCommand(string? FullName, string? CompanyName, string? Phone, AddressSnapshot? BillingAddress, string? RegistrationNumber);
public interface ICustomerProfileService { Task<Result> UpdateProfileAsync(long customerId, UpdateProfileCommand command, string correlationId, CancellationToken ct = default); }
public sealed record CustomerProfileView([Snowflake] long Id, string Email, bool EmailVerified, string? Phone, string? FullName, string? CompanyName,
  AddressSnapshot? BillingAddress, string? RegistrationNumber, string Status);
public interface ICustomerShopQueries { Task<Result<CustomerProfileView>> GetProfileAsync(long customerId, CancellationToken ct = default); }
public static class ShopIdentity { public static bool TryRead(ClaimsPrincipal user, out Guid sub, out string email, out bool emailVerified); } // tenant Shop, sub Guid, email non-empty
```
REST (group `/api/v1/shop`, `.RequireAuthorization(AuthPolicies.ShopCustomer)`):
`GET /me` → `ShopIdentity.TryRead` (failure → `EndpointResult.Forbidden()`) →
`IWebsiteCustomerPort.EnsureFromIdentityAsync` → `GetProfileAsync`.
`PUT /me` body `UpdateProfileRequest(fullName?, companyName?, phone?, billingAddress?, registrationNumber?)`
→ ensure → `UpdateProfileAsync(customerId, …, correlationId)` → 200 with the profile. Email is not
in the request; Keycloak owns it.
GraphQL: `me: CustomerProfile` and `updateProfile(input): CustomerProfile`, both
`[Authorize(Policy = AuthPolicies.ShopCustomer)]`, with `ClaimsPrincipal` injected.

- [ ] **Step 1: Write the failing tests.**
```csharp
[Fact] public async Task First_Me_Call_Creates_The_Website_Customer_Lazily()
{ /* CreateShopToken(sub, "c@x.com", false) → GET /api/v1/shop/me 200 {email c@x.com, emailVerified false}; repeat → same id */ }
[Fact] public async Task Me_Mirrors_Email_Verification_From_The_Token() { /* first false, then token with true → emailVerified true */ }
[Fact] public async Task Update_Profile_Persists_And_Audits_Phone() { /* PUT phone "+65 9123 4567" → "+6591234567"; admin detail shows ContactChange(Phone) */ }
[Fact] public async Task Invalid_Phone_Is_400_Validation_Failed() { }
[Fact] public async Task Backoffice_Token_Cannot_Call_Shop_Me() { /* 403 */ }
[Fact] public async Task GraphQL_Me_And_UpdateProfile_Match_Rest() { }
```
- [ ] **Step 2: Run** `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter "FullyQualifiedName~ShopProfileTests"`. Expected: FAIL (route not found).
- [ ] **Step 3: Implement** as specified above.
- [ ] **Step 4: Run** the Step 2 command. Expected: PASS.
- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Customer tests/IntegrationTests/Customers
git commit -m "feat(customer): shop me/updateProfile over REST and GraphQL with lazy creation" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 14: `ICustomerAdminService` and admin list/detail with contact history

**Files:**
- Create: `src/Modules/Customer/Application/Customers/Ports/Inbound/ICustomerAdminService.cs`, `Services/CustomerAdminService.cs`
- Create: `src/Modules/Customer/QueryApplication/Customers/ICustomerAdminQueries.cs` (implemented in `CustomerQueries.cs`)
- Create: `src/Modules/Customer/Adapter/Inbound/Rest/Admin/CustomerAdminEndpoints.cs`, `Adapter/Inbound/GraphQL/Admin/{CustomerAdminQueries,CustomerAdminMutations}.cs`
- Test: `tests/IntegrationTests/Customers/CustomerAdminApiTests.cs`

**Interfaces:**
```csharp
public interface ICustomerAdminService {   // Staff or Super Admin; audited ("customer.suspended", "customer.reactivated")
  Task<Result> SuspendAsync(long customerId, AdminActor actor, CancellationToken ct = default);
  Task<Result> ReactivateAsync(long customerId, AdminActor actor, CancellationToken ct = default);
}
public sealed record AdminCustomerListFilter(string? Kind, string? Status, long? PartnerId, string? Search, int Page, int PageSize); // Search: ILIKE on email, phone, full_name, company_name, external_customer_id
public sealed record CustomerAdminListItem([Snowflake] long Id, string Kind, [Snowflake] long? PartnerId, string? ExternalCustomerId, string? Email, string? Phone,
  string? FullName, string? CompanyName, string Status, DateTimeOffset CreatedAt);
public sealed record ContactChangeView(string Field, string? OldValue, string? NewValue, DateTimeOffset ChangedAt, [Snowflake] long? SourcePartnerId, string CorrelationId);
public sealed record CustomerAdminDetail(CustomerAdminListItem Customer, bool EmailVerified, AddressSnapshot? BillingAddress, string? RegistrationNumber,
  Guid? IdentityUserId, IReadOnlyList<ContactChangeView> ContactHistory); // newest first
public interface ICustomerAdminQueries {
  Task<PaginatedQuery<CustomerAdminListItem>> ListAsync(AdminCustomerListFilter filter, CancellationToken ct = default); // pageSize 1–100, newest first
  Task<Result<CustomerAdminDetail>> GetAsync(long customerId, CancellationToken ct = default);
}
```
REST (group `/api/v1/admin/customers`, `.RequireAuthorization(AuthPolicies.AdminStaff).RequirePermission(AdminPermissions.CustomersManage)`):
`GET /` (query params bound one by one, so no DTO carries `PartnerId`), `GET /{id}`,
`POST /{id}/suspend`, `POST /{id}/reactivate`.
GraphQL: `adminCustomers(filter: AdminCustomerListInput)`, `adminCustomer(id)`,
`suspendCustomer(id)`, `reactivateCustomer(id)` with `[Authorize(Policy = AuthPolicies.AdminStaff)]`
+ `[RequirePermission(AdminPermissions.CustomersManage)]`. The input type is named with the `Admin`
prefix; Task 15's arch test relies on it.

- [ ] **Step 1: Write the failing tests** (staff token — staff is allowed here).
```csharp
[Fact] public async Task Staff_Lists_Website_And_Partner_Linked_Customers_With_Filters() { /* kind=PartnerLinked&partnerId=7 → only that partner's */ }
[Fact] public async Task Detail_Shows_Contact_History_Newest_First_With_Source_Partner_And_Correlation() { }
[Fact] public async Task Suspend_Blocks_Shop_Profile_Update() { /* suspend → shop PUT /me → 403 CUSTOMER_SUSPENDED; WebsiteCustomerRef.IsActive false */ }
[Fact] public async Task Search_Matches_Phone_And_External_Id() { }
[Fact] public async Task GraphQL_AdminCustomer_Returns_Contact_History() { }
```
- [ ] **Step 2: Run** `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter "FullyQualifiedName~CustomerAdminApiTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** as specified above.
- [ ] **Step 4: Run** the Step 2 command. Expected: PASS.
- [ ] **Step 5: Commit.**
```bash
git add src/Modules/Customer tests/IntegrationTests/Customers
git commit -m "feat(customer): admin list/detail with contact history; suspend/reactivate" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 15: Architecture — partner id only via `PartnerPrincipal`; module list

**Files:**
- Create: `tests/ArchitectureTests/PartnerTenancyArchitectureTests.cs`
- Modify: `tests/ArchitectureTests/HexagonalBoundaryTests.cs` (confirm `ModuleNames` contains `"Partners"`, `"Settings"`, `"Customer"`; Partners/Settings/Customer add **no** exception rows — spec §3.6 says they reference no other module)
- Modify: `tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj` (reference the Partners/Settings/Customer Installers if not already)

**Interfaces:** consumes `HexagonalBoundaryTests.ModuleNames` (make it `internal static` if phase 0 left it private).

- [ ] **Step 1: Write the test.**
```csharp
public class PartnerTenancyArchitectureTests
{
    private static readonly string[] RequestSuffixes = ["Request", "Input", "Body", "Command"];

    [Fact]
    public void No_Inbound_Request_Dto_Has_A_PartnerId_Property()
    {
        var inbound = InboundAssemblies().ToList();
        inbound.Should().NotBeEmpty();
        var offenders = inbound.SelectMany(a => a.GetTypes())
            .Where(t => RequestSuffixes.Any(s => t.Name.EndsWith(s, StringComparison.Ordinal)))
            .Where(t => !t.Name.StartsWith("Admin", StringComparison.Ordinal))   // admin filters may name a partner; they never act as one
            .Where(t => t.GetProperties().Any(p => p.Name.Equals("PartnerId", StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.FullName).ToList();
        offenders.Should().BeEmpty("the partner id reaches partner-facing ports only through PartnerPrincipal, built from the API-key claims (spec §6.2)");
    }

    [Fact]
    public void Every_Module_Inbound_Rest_Assembly_Is_Scanned()
        => InboundAssemblies().Select(a => a.GetName().Name).Should().Contain(HexagonalBoundaryTests.ModuleNames
            .Select(m => $"EwaysVNext.Modules.{m}.Adapter.Inbound.Rest"));

    private static IEnumerable<Assembly> InboundAssemblies() =>
        HexagonalBoundaryTests.ModuleNames.SelectMany(m => new[] { "Rest", "GraphQL" }.Select(k => $"EwaysVNext.Modules.{m}.Adapter.Inbound.{k}"))
            .Select(TryLoad).OfType<Assembly>();
    private static Assembly? TryLoad(string name) { try { return Assembly.Load(name); } catch (FileNotFoundException) { return null; } }
}
```
- [ ] **Step 2: Run** `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter "FullyQualifiedName~PartnerTenancyArchitectureTests"`. Expected: PASS on the current code. Mutation check: add `public sealed record UpdateProfileRequestProbe(long PartnerId);` to `Customer.Adapter.Inbound.Rest` → `No_Inbound_Request_Dto_Has_A_PartnerId_Property` FAILS, naming it. Delete the probe → PASS.
- [ ] **Step 3: Run** the full architecture suite `dotnet test --project tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj`. Expected: PASS.
- [ ] **Step 4: Commit.**
```bash
git add tests/ArchitectureTests
git commit -m "test(arch): no inbound request DTO carries PartnerId; Partners/Settings/Customer in ModuleNames" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 16: Acceptance 13 — staff cannot manage partners, settings, FX or refunds

**Files:**
- Create: `tests/IntegrationTests/Identity/Acceptance13StaffPermissionTests.cs`

**Interfaces:** consumes every admin surface built in this phase. Later phases **extend this
class**: phase 2–3 adds FX override and currency enablement rows (`AdminPermissions.FxManage`,
`CurrenciesManage`), phase 4–5 adds refund/credit-note rows (`RefundsManage`). Their routes do not
exist yet, so this phase covers partners, keys, settings and the RBAC GraphQL. FX and refunds are
covered here only by the matrix unit test in Task 1.

- [ ] **Step 1: Write the tests.**
```csharp
public static TheoryData<string, string> StaffForbiddenRest => new()
{
    { "POST", "/api/v1/admin/partners" },
    { "GET",  "/api/v1/admin/partners" },
    { "POST", "/api/v1/admin/partners/{partnerId}/keys" },
    { "POST", "/api/v1/admin/partners/{partnerId}/keys/{keyId}/rotate" },
    { "POST", "/api/v1/admin/partners/{partnerId}/keys/{keyId}/revoke" },
    { "POST", "/api/v1/admin/partners/{partnerId}/suspend" },
    { "PUT",  "/api/v1/admin/settings/seller" },
    { "POST", "/api/v1/admin/settings/payment-instructions" },
    { "PUT",  "/api/v1/admin/settings/commerce-limits" },
};
[Theory, MemberData(nameof(StaffForbiddenRest))]
public async Task Staff_Is_Forbidden(string method, string route)
{
    var response = await Send(method, Resolve(route), CreateStaffToken(), ValidBodyFor(route));
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    // and the state is unchanged: partner count, key list, settings snapshot equal before/after
}
[Theory]
[InlineData("mutation { createPartner(input:{name:\"x\", scopes:[\"orders:read\"]}) { id } }")]
[InlineData("mutation { updateCommerceLimits(input:{minUsd:\"1.00\", maxUsd:\"2.00\", bulkDefaultMax:1, compositionDeadlineMs:1000, compositionMaxLines:1}) { minUsd } }")]
[InlineData("mutation { createPermission(input:{code:\"x\", name:\"x\", rules:[]}) { permission { id } } }")]  // RBAC is Super-only now
public async Task Staff_Is_Forbidden_On_GraphQL(string query)
{ /* error code AUTH_NOT_AUTHORIZED (HTTP 403 via GraphQLHttpResponseFormatter) */ }
[Fact] public async Task Super_Admin_Is_Allowed_On_The_Same_Routes() { /* positive control: every row above → 2xx with super admin token */ }
[Fact] public async Task Staff_Can_Manage_Customers() { /* GET /api/v1/admin/customers → 200; suspend → 200 */ }
[Fact] public async Task Shop_Token_With_A_Forged_super_admin_Role_Is_Forbidden() { /* CreateToken("Shop", "super_admin") on POST partners → 403 */ }
```
- [ ] **Step 2: Run** `dotnet test --project tests/IntegrationTests/EwaysVNext.IntegrationTests.csproj --filter "FullyQualifiedName~Acceptance13StaffPermissionTests"`. Expected: PASS (the gates exist since Tasks 1, 8 and 10). Mutation check: replace `AuthPolicies.AdminSuper` with `AdminStaff` on the partners group **and** add the staff role to the `partners.manage` matrix row → `Staff_Is_Forbidden` fails for partner rows. Restore.
- [ ] **Step 3: Commit.**
```bash
git add tests/IntegrationTests/Identity
git commit -m "test(acceptance-13): staff cannot manage partners, keys, settings or RBAC" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Task 17: Phase gate

**Files:**
- Create: `docs/reports/phase-1.md`
- Modify: `AGENTS.md` (modules list: Partners, Settings, Customer as rebuilt; the `PartnerApiKey` scheme; policy names; endpoints table rows for `/api/v1/admin/partners`, `/api/v1/admin/settings`, `/api/v1/admin/customers`, `/api/v1/shop/me`), `docs/TESTING-GUARANTEES.md` (new suites; what the probe endpoints do and do not prove), `src/Authorization/README.md` (if not completed in Task 1)

- [ ] **Step 1:** `dotnet build EwaysVNext.slnx` → zero warnings-as-errors.
- [ ] **Step 2:** `dotnet test EwaysVNext.slnx` (Docker running) → all green. Record the counts per suite.
- [ ] **Step 3:** `dotnet run --project src/Host/EwaysVNext.Host -- --migrate` against the dev compose Postgres → applies `Partners`, `PartnerUsage`, `Settings`, `CustomerRebuild` and `AdminPermissionMatrix`. Then `docker compose -f build/compose/docker-compose.dev.yml up keycloak` imports both realms without errors (check the Keycloak log for `Realm 'Shop' imported`, `Realm 'Backoffice' imported`).
- [ ] **Step 4:** Write `docs/reports/phase-1.md`:
  - **What works:** realms, matrix, partner keys end to end, usage log, settings, customer.
  - **Incomplete:** FX and refund rows of acceptance 13 land in phases 2–3 and 4–5; no Keycloak in automated tests (realms are checked structurally only).
  - **Risks:** the in-process rate limiter (ADR-0010 caveat); one DB lookup per partner request, with no cache so that revocation is immediate; usage entries dropped when the channel is full (metered).
  - **Reproduction commands:** the ones above.
- [ ] **Step 5: Commit.**
```bash
git add docs AGENTS.md src/Authorization/README.md
git commit -m "docs: phase 1 report; AGENTS.md and testing guarantees for identity and tenancy" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Additions to the cross-phase contracts (produced here, consumed later)

```csharp
// EwaysVNext.Core.Application.Security
EwaysClaimTypes { Tenant, Subject, Email, EmailVerified, PartnerId="partner_id", ApiKeyId="api_key_id", Scope="scope",
                  RateLimitPerMinute="rate_limit_per_minute", BulkMaxItems="bulk_max_items" }   // Sales reads bulk_max_items for POST /orders/bulk
AdminRoles { SuperAdmin="super_admin", Staff="staff" }
AdminActor.TryFrom(ClaimsPrincipal user, out AdminActor actor)
// EwaysVNext.Presentation.Endpoint.Security
AuthPolicies { AdminStaff="admin:staff", AdminSuper="admin:super", ShopCustomer="shop:customer", Partner(scope) => "partner:{scope}" }
AuthSchemes { PartnerApiKey="PartnerApiKey", PartnerKeyPrefix="pk_" }
// AuthorizationService.Abstractions.Policies
AdminPermissions { CatalogManage, ImportsManage, CustomersManage, OrdersManage, PaymentsConfirm,   // staff
                   PartnersManage, FxManage, CurrenciesManage, SettingsManage, RefundsManage, AdminAccountsManage } // super
// EwaysVNext.Persistence
// (from phase 0) enum SaveOutcome { Saved, UniqueViolation, ConcurrencyConflict };  IUnitOfWork.TrySaveChangesAsync(CancellationToken ct = default)
// Settings
SellerSnapshot.RequiredBuyerFields entries are '|'-joined alternatives over
  { email, phone, fullName, companyName, billingAddress, registrationNumber }; default ["email|phone"].
// Errors and status codes
401 body code "UNAUTHORIZED"; 403 "FORBIDDEN"; 403 "INSUFFICIENT_SCOPE"; 429 "RATE_LIMITED" (retryable, Retry-After: 60);
CUSTOMER_SUSPENDED (403)
```
Admin endpoint convention: group `.RequireAuthorization(AuthPolicies.AdminStaff | AdminSuper)` +
per-endpoint `.RequirePermission(AdminPermissions.X)`; GraphQL `[Authorize(Policy=…)]` +
`[RequirePermission(…)]`. Partner endpoints: `.RequireAuthorization(AuthPolicies.Partner(PartnerScopes.X))`
and a `PartnerPrincipal` from `PartnerPrincipal.TryFrom(user, …)`.

## Spec conflicts and resolutions

1. **Claim name `key_id` (spec §6.2) vs `api_key_id` (master contract).** The master contract
   wins: `api_key_id`.
2. **`ICustomerResolutionPort.ResolvePartnerCustomer` (spec §5.2) vs the split
   Find/Create/Sync/GetInvoiceParty (master).** The master contract wins. It also matches §10.3,
   where create happens before the order save and sync only after it.
3. **`Fx:MaxRateAge` under `CommerceLimits` (spec §5.7).** Pricing cannot reference Settings
   (spec §3.6), so storing it in Settings would give two sources of truth. It stays the
   `Fx:MaxRateAge` config value and is surfaced read-only in `CommerceLimitsSnapshot.FxMaxRateAge`.
4. **`ApiKey.LastUsedAt` (spec §5.1).** It is not stored; it is derived as `max(api_key_usage.at)`.
   A stored column would mean a hot write on every partner request, plus `xmin` conflicts with
   admin edits. After the 90-day prune it shows null ("not used in 90 days").
5. **`BulkDefaultMax` (Settings) vs `Partner.BulkMaxItems` default 100 (spec §5.1).** Partners
   references no module, so new partners default to `PartnerLimits.DefaultBulkMaxItems = 100`.
   `CommerceLimits.BulkDefaultMax` stays as the Sales-side fallback.
6. **"Startup fails fast if SMTP variables are missing in Production" (spec §6.1).** Implemented in
   production compose with `${KC_SMTP_*:?}`, since Keycloak, not the API, is the SMTP client.
7. **`PaymentInstruction` "versioned, one active".** Modelled as one aggregate with version
   children, so activating a version is one single-aggregate save (rule 7), not two rows toggled
   across aggregates.
8. **`GstRegistered` setting.** It is stored, but `true` is refused until a GST `ITaxPolicy`
   exists, so the anti-goal "charges GST / calls it a tax invoice" cannot be reached through
   settings.
