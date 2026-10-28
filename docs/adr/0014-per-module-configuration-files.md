# ADR-0014: Per-Module Configuration Files Under ModuleAppSetting/

## Status
Accepted — 2026-09-01.

## Context
Every module's settings (today, just a connection string) lived in the Host's single shared
`appsettings.json`. That file had already drifted: its `ConnectionStrings` block carried
`Identity` and `Orders` entries for modules that do not exist in this repository, while
`Customer`'s connection string was missing entirely (supplied only via the
`ConnectionStrings__Customer` environment variable in `docker-compose.yml`, with no local
default). A shared settings file has no natural boundary to stop that kind of drift — nothing
stops one module's config from being added, forgotten, or duplicated by whoever touches the
file next, and there is no single place to look for "everything Catalog needs to run."

## Decision
**Each module gets its own settings folder and its own file per environment, loaded by a
dedicated `IConfigurationBuilder` extension.**

```
src/Host/EwaysVNext.Host/
├── ConfigurationExtensions.cs
└── ModuleAppSetting/
    ├── Catalog/
    │   ├── modules.catalog.json              (required — base defaults)
    │   ├── modules.catalog.Development.json  (optional — environment override)
    │   ├── modules.catalog.Staging.json
    │   └── modules.catalog.Production.json
    ├── Customer/  (same four-file shape)
    └── Admin/     (same four-file shape)
```

`ConfigurationExtensions.AddModuleConfiguration(moduleNames, environmentName)` loads, per
module, `ModuleAppSetting/{Capitalized}/modules.{module}.json` (`optional: false` — the base
file must exist) and, if an environment name is supplied,
`modules.{module}.{environmentName}.json` (`optional: true` — only the file matching the
running environment needs to exist, but all three are scaffolded up front so the shape is
obvious to whoever adds a module). `reloadOnChange: true` on both, matching how ASP.NET Core's
own `appsettings.json`/`appsettings.{Environment}.json` already behave.

Called from `Program.cs` immediately after `WebApplication.CreateBuilder(args)`:

```csharp
builder.Configuration.AddModuleConfiguration(["catalog", "customer", "admin"], builder.Environment.EnvironmentName);
builder.Configuration.AddEnvironmentVariables();
```

### The precedence trap, and why the second line is not optional
`CreateBuilder(args)` already establishes the default configuration order (later = higher
precedence): `appsettings.json` → `appsettings.{Environment}.json` → environment variables →
command-line args. `IConfigurationBuilder.AddJsonFile` always appends — it cannot insert a
source in the middle of that list. Calling `AddModuleConfiguration` right after `CreateBuilder`
and stopping there would put the new module files **after** environment variables, giving them
the highest precedence in the whole configuration. Since each module's base file ships an
empty `ConnectionStrings:{Module}` string (the same "no default, must be supplied" convention
`appsettings.json` already used), that would mean the empty JSON default silently overrides
`ConnectionStrings__Catalog`/`ConnectionStrings__Customer` from `docker-compose.yml` — every
Docker deployment would fall back to a blank connection string.

Re-adding `.AddEnvironmentVariables()` immediately after `AddModuleConfiguration` exploits the
same last-source-wins rule to restore the correct order: environment variables become the
last-added source again and regain precedence over the module JSON files. This does reorder
environment variables to rank above command-line arguments (previously the highest-precedence
source) — accepted, since nothing in this Host reads a `key=value`-style command-line
argument; the only argument this process inspects is the positional `--migrate` flag, checked
via `args.Contains(...)`, which is unaffected by configuration source order.

Verified at runtime, not just at compile time: with no `ConnectionStrings__Catalog` set,
`--migrate` falls back to the InMemory provider in Development (the module file's empty
string wins, as expected). With `ConnectionStrings__Catalog` set to a fake host, the app
attempts a real SQL connection to it instead of silently using the empty default — confirming
the environment variable, not the JSON file, wins.

## Consequences
- One folder per module is now "everything this module needs to run" — easy to find, easy to
  audit, and a new module's settings cannot be dropped into the wrong place by accident the way
  a shared file invites.
- The stale `Identity`/`Orders` entries and the missing `Customer` entry are gone;
  `appsettings.json` now carries only truly cross-module settings (`Jwt`, `Host`, `Serilog`,
  `Logging`).
- Adding a module means adding both its `Installer` registration in `Program.cs` **and** its
  entry in the `moduleNames` array passed to `AddModuleConfiguration` — miss the second one and
  the module falls back to no settings at all rather than failing loudly, since only the array
  entries get a base file loaded. There is no test enforcing the two stay in sync; a reviewer
  must catch it.
- Any future `IConfigurationBuilder` source added after `AddModuleConfiguration` (another
  `AddJsonFile`, `AddInMemoryCollection`, etc.) needs the same precedence awareness — it will
  rank above environment variables unless deliberately placed before them or reasoned about
  the same way this ADR reasons about `AddEnvironmentVariables()`.

## References
- `src/Host/EwaysVNext.Host/ConfigurationExtensions.cs` — the extension method
- `src/Host/EwaysVNext.Host/Program.cs` — call site and the precedence-restoring line
- `docker-compose.yml` — the environment-variable source this ADR's fix protects
