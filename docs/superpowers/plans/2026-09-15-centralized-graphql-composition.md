# Centralized GraphQL Composition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run all module-owned GraphQL types through one Admin-owned Hot Chocolate server at `/api/v1/admin/graphql`.

**Architecture:** Admin's inbound GraphQL adapter creates and maps the single server. Catalog keeps its resolver types and exposes an explicit `IRequestExecutorBuilder` contribution through its Installer; Host composes that public contribution without referencing concrete resolver classes.

**Tech Stack:** .NET 10, ASP.NET Core, Hot Chocolate 16.6.2, xUnit v3, AwesomeAssertions

**Spec:** `docs/superpowers/specs/2026-09-15-centralized-graphql-composition-design.md`

## Global Constraints

- Keep all Catalog queries, mutations, and future Catalog-specific GraphQL types inside Catalog.
- Keep exactly one `AddGraphQLServer()` invocation, owned by Admin.
- Use explicit compile-time-safe contribution methods; do not add reflection, assembly scanning, marker interfaces, or a generic runtime registry.
- Host and Admin must not reference `CategoryAdminQueries` or `CategoryAdminMutations`.
- Remove `/api/v1/admin/category/graphql` completely; `/api/v1/admin/graphql` is the only GraphQL endpoint.
- Preserve Admin authorization, `admin` rate limiting, `long` to GraphQL `ID` binding, request options, resolver behavior, and error mapping.
- Do not move resolver types or introduce unrelated behavior changes.

---

### Task 1: Pin Central Composition and Endpoint Behavior with Failing Tests

**Files:**
- Modify: `tests/UnitTests/CategoryApiAdapterTests.cs`
- Modify: `tests/UnitTests/EwaysVNext.UnitTests.csproj`
- Modify: `tests/ArchitectureTests/CategoryArchitectureTests.cs`

**Interfaces:**
- Consumes: current Catalog-owned `AddCategoryAdminGraphQl()` and `MapCategoryAdminGraphQl()` behavior.
- Produces: executable and source-level requirements for `AddAdminGraphQlServer()`, `AddCatalogGraphQlTypes(IRequestExecutorBuilder)`, and `MapAdminGraphQl()`.

- [ ] **Step 1: Change the API fixture and endpoint assertions to require central composition**

Add the Admin and Catalog Installer namespaces, then change the fixture setup to the approved public APIs:

```csharp
builder.Services
    .AddAdminGraphQlServer()
    .AddCatalogGraphQlTypes();

var app = builder.Build();
app.MapAdminGraphQl();
```

Change all executor acquisition to the single default executor:

```csharp
var executor = await fixture.App.Services.GetRequestExecutorAsync();
```

Require the canonical route and reject the removed route:

```csharp
fixture.Endpoints.Should().ContainSingle(endpoint =>
    endpoint.RoutePattern.RawText == "/api/v1/admin/graphql");
fixture.Endpoints.Should().NotContain(endpoint =>
    endpoint.RoutePattern.RawText!.StartsWith(
        "/api/v1/admin/category/graphql", StringComparison.Ordinal));
```

Keep the existing endpoint metadata assertions for the `Admin` policy and `admin` rate-limit policy.

- [ ] **Step 2: Reference module Installer APIs from the unit-test project**

Add these project references without removing the direct Catalog GraphQL reference used by resolver-level tests:

```xml
<ProjectReference Include="..\..\src\Modules\Admin\Installer\EwaysVNext.Modules.Admin.Installer.csproj" />
<ProjectReference Include="..\..\src\Modules\Catalog\Installer\EwaysVNext.Modules.Catalog.Installer.csproj" />
```

- [ ] **Step 3: Add architecture tests for the approved invariants**

Add tests that scan only `src/**/*.cs` and assert:

```csharp
graphQlSources.Count(source => source.Text.Contains("AddGraphQLServer(", StringComparison.Ordinal))
    .Should().Be(1);
graphQlSources.Single(source => source.Text.Contains("AddGraphQLServer(", StringComparison.Ordinal))
    .Path.Should().Contain(Path.Combine("Admin", "Adapter", "Inbound", "GraphQL"));
```

Also assert:

- `MapGraphQL(` and `MapNitroApp(` appear only in Admin's inbound GraphQL adapter;
- Host source contains neither `CategoryAdminQueries` nor `CategoryAdminMutations`;
- Host project has no direct reference to `Catalog.Adapter.Inbound.GraphQL.csproj`;
- Catalog resolver files still exist under `Catalog/Adapter/Inbound/GraphQL/Categories`;
- no source file contains `/api/v1/admin/category/graphql`.

- [ ] **Step 4: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~CategoryApiAdapterTests"
dotnet test tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter "FullyQualifiedName~CategoryArchitectureTests"
```

Expected: compilation or assertions fail because the new public composition methods do not exist and Catalog still owns the server and old endpoint.

- [ ] **Step 5: Commit the failing tests**

```powershell
git add tests/UnitTests/CategoryApiAdapterTests.cs tests/UnitTests/EwaysVNext.UnitTests.csproj tests/ArchitectureTests/CategoryArchitectureTests.cs
git commit -m "test: require centralized GraphQL composition"
```

---

### Task 2: Implement Admin Server and Catalog Type Contribution

**Files:**
- Modify: `src/Modules/Admin/Adapter/Inbound/GraphQL/DependencyInjection.cs`
- Modify: `src/Modules/Admin/Installer/ModuleInstaller.cs`
- Modify: `src/Modules/Catalog/Adapter/Inbound/GraphQL/DependencyInjection.cs`
- Modify: `src/Modules/Catalog/Installer/ModuleInstaller.cs`
- Modify: `src/Host/EwaysVNext.Host/Program.cs`
- Modify: `src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj`

**Interfaces:**
- Consumes: `IServiceCollection`, `IRequestExecutorBuilder`, `IEndpointRouteBuilder`, and Catalog's existing resolver extensions.
- Produces: `AddAdminGraphQlServer(IServiceCollection)`, `MapAdminGraphQl(IEndpointRouteBuilder)`, and `AddCatalogGraphQlTypes(IRequestExecutorBuilder)` exposed from module Installer namespaces.

- [ ] **Step 1: Replace Catalog server ownership with a type-only contribution**

In Catalog's GraphQL adapter, remove server constants, `IServiceCollection` registration, endpoint mapping, authorization/rate-limit mapping, root creation, and global options. Retain only:

```csharp
public static IRequestExecutorBuilder AddCatalogGraphQlTypes(
    this IRequestExecutorBuilder builder) => builder
    .AddTypeExtension<CategoryAdminQueries>()
    .AddTypeExtension<CategoryAdminMutations>();
```

The resolver files remain unchanged and in their existing Catalog directory.

- [ ] **Step 2: Expose the Catalog contribution through Catalog Installer**

Remove `services.AddCategoryAdminGraphQl()` from `AddCatalogModule()`. Add a public Installer-level facade:

```csharp
public static IRequestExecutorBuilder AddCatalogGraphQlTypes(
    this IRequestExecutorBuilder builder) =>
    Adapter.Inbound.GraphQL.DependencyInjection.AddCatalogGraphQlTypes(builder);
```

Use the fully qualified adapter call to prevent extension-method ambiguity. This lets Host use `EwaysVNext.Modules.Catalog` without naming concrete resolver types or directly referencing the GraphQL adapter project.

- [ ] **Step 3: Implement the single Admin-owned server and endpoint mapping**

In Admin's GraphQL adapter, replace the no-op methods with:

```csharp
public const string EndpointPath = "/api/v1/admin/graphql";
public const string NitroEndpointPath = "/api/v1/admin/graphql/ui";

public static IRequestExecutorBuilder AddAdminGraphQlServer(
    this IServiceCollection services) => services
    .AddGraphQLServer()
    .BindRuntimeType<long, IdType>()
    .ModifyRequestOptions(options => options.IncludeExceptionDetails = true)
    .AddAuthorization()
    .AddQueryType(descriptor => descriptor.Name("Query"))
    .AddMutationType(descriptor => descriptor.Name("Mutation"));

public static IEndpointConventionBuilder MapAdminGraphQl(
    this IEndpointRouteBuilder endpoints)
{
    endpoints.MapNitroApp(NitroEndpointPath)
        .WithOptions(options => options.GraphQLEndpoint = EndpointPath);

    return endpoints.MapGraphQL(EndpointPath)
        .RequireAuthorization("Admin")
        .RequireRateLimiting("admin");
}
```

This relocates existing global behavior without altering it.

- [ ] **Step 4: Expose central GraphQL composition through Admin Installer**

Remove the old `services.AddAdminGraphQL()` call from `AddAdminModule()`. Add Installer-level facades that delegate to Admin's inbound GraphQL adapter:

```csharp
public static IRequestExecutorBuilder AddAdminGraphQlServer(
    this IServiceCollection services) =>
    Adapter.Inbound.GraphQL.DependencyInjection.AddAdminGraphQlServer(services);

public static IEndpointConventionBuilder MapAdminGraphQl(
    this IEndpointRouteBuilder endpoints) =>
    Adapter.Inbound.GraphQL.DependencyInjection.MapAdminGraphQl(endpoints);
```

- [ ] **Step 5: Compose the server and module contribution in Host**

After module service registration, compose GraphQL through module public APIs:

```csharp
builder.Services
    .AddAdminGraphQlServer()
    .AddCatalogGraphQlTypes();
```

Replace the Catalog endpoint and Host-owned Nitro block with:

```csharp
app.MapAdminGraphQl();
```

Remove Host `using` directives for the concrete Admin/Catalog GraphQL adapter namespaces. Do not register `CategoryAdminQueries` or `CategoryAdminMutations` in Host.

- [ ] **Step 6: Remove the direct Host-to-Catalog-GraphQL project reference**

Delete only this reference from the Host project:

```xml
<ProjectReference Include="..\..\Modules\Catalog\Adapter\Inbound\GraphQL\EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.csproj" />
```

Keep the Catalog Installer reference, which exposes the contribution API and owns the adapter dependency.

- [ ] **Step 7: Run focused tests and verify GREEN**

Run:

```powershell
dotnet test tests/UnitTests/EwaysVNext.UnitTests.csproj --filter "FullyQualifiedName~CategoryApiAdapterTests"
dotnet test tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj --filter "FullyQualifiedName~CategoryArchitectureTests"
```

Expected: all selected tests pass, including large Snowflake ID serialization, field authorization, canonical route metadata, and source architecture assertions.

- [ ] **Step 8: Commit the implementation**

```powershell
git add src/Modules/Admin/Adapter/Inbound/GraphQL/DependencyInjection.cs src/Modules/Admin/Installer/ModuleInstaller.cs src/Modules/Catalog/Adapter/Inbound/GraphQL/DependencyInjection.cs src/Modules/Catalog/Installer/ModuleInstaller.cs src/Host/EwaysVNext.Host/Program.cs src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj
git commit -m "refactor: centralize GraphQL server composition"
```

---

### Task 3: Complete Verification

**Files:**
- Verify only; modify earlier task files only if a directly related failure identifies a defect.

**Interfaces:**
- Consumes: the central Admin server, Catalog contribution API, and updated tests.
- Produces: evidence that the approved architecture and existing Category behavior are intact.

- [ ] **Step 1: Run source invariants**

```powershell
rg -n "AddGraphQLServer\(" src -g '*.cs'
rg -n "MapGraphQL\(|MapNitroApp\(" src -g '*.cs'
rg -n "/api/v1/admin/category/graphql|CategoryAdminQueries|CategoryAdminMutations" src/Host -g '*.cs' -g '*.csproj'
```

Expected: one `AddGraphQLServer()` in Admin; endpoint/Nitro mapping only in Admin; no old route or concrete Catalog resolver names in Host.

- [ ] **Step 2: Run all relevant tests**

```powershell
dotnet test tests/UnitTests/EwaysVNext.UnitTests.csproj
dotnet test tests/ArchitectureTests/EwaysVNext.ArchitectureTests.csproj
```

Expected: all tests pass. Any unrelated pre-existing failure must be reported and kept separate.

- [ ] **Step 3: Build affected projects and solution**

```powershell
dotnet build src/Modules/Catalog/Adapter/Inbound/GraphQL/EwaysVNext.Modules.Catalog.Adapter.Inbound.GraphQL.csproj
dotnet build src/Modules/Catalog/Installer/EwaysVNext.Modules.Catalog.Installer.csproj
dotnet build src/Modules/Admin/Adapter/Inbound/GraphQL/EwaysVNext.Modules.Admin.Adapter.Inbound.GraphQL.csproj
dotnet build src/Modules/Admin/Installer/EwaysVNext.Modules.Admin.Installer.csproj
dotnet build src/Host/EwaysVNext.Host/EwaysVNext.Host.csproj
dotnet build EwaysVNext.slnx
```

Expected: every affected project and the solution build successfully.

- [ ] **Step 4: Check the final diff and repository state**

```powershell
git diff --check HEAD~2..HEAD
git status --short --branch
git log -3 --oneline
```

Expected: no whitespace errors, only approved files changed, and no uncommitted implementation files remain.
