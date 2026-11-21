# Web Application Local Development

## Prerequisites

- .NET SDK selected by `global.json`
- Node.js 24 or newer
- pnpm 11.25.0
- Docker for PostgreSQL, MinIO and Keycloak

## Start the stack

```text
docker compose -f build/compose/docker-compose.dev.yml up -d
dotnet run --project src/Host/Mkbj.Host -- --migrate
dotnet run --project src/Host/Mkbj.Host -- --seed
dotnet run --project src/Host/Mkbj.Host
```

Migrations are explicit; the host does not apply them at startup. Seeding is for non-production environments only.

If a host PostgreSQL service already owns port 5432, bind the container to another host port before starting it:

```powershell
$env:POSTGRES_PORT = '5433'
docker compose -p mkbj-dev -f build/compose/docker-compose.dev.yml up -d
$env:ConnectionStrings__Default = 'Host=localhost;Port=5433;Database=mkbj;Username=mkbj_migrator;Password=<local-migrator-password>'
dotnet run --project src/Host/Mkbj.Host -- --migrate
```

Containers still communicate with PostgreSQL on port 5432 inside the Compose network. The override only changes the host-facing port.

To deliberately rebuild the MKBJ development data from zero, remove only this project and its named volumes, then start it again:

```text
docker compose -p mkbj-dev -f build/compose/docker-compose.dev.yml down --volumes --remove-orphans
docker compose -p mkbj-dev -f build/compose/docker-compose.dev.yml up -d
```

This permanently removes the local MKBJ database and object-storage contents. It does not touch unrelated Docker projects.

## Configure the browser applications

Copy `apps/admin-web/.env.example` to `apps/admin-web/.env.local`. Defaults match the repository launch profile:

```text
VITE_API_BASE_URL=http://localhost:8608
VITE_GRAPHQL_URL=http://localhost:8608/graphql
VITE_KEYCLOAK_URL=http://localhost:8081
VITE_KEYCLOAK_REALM=Backoffice
VITE_KEYCLOAK_CLIENT_ID=backoffice-console
```

For the shop, copy `apps/shop-web/.env.example` to `apps/shop-web/.env.local` only when overriding these defaults:

```text
MKBJ_API_GRAPHQL_URL=http://localhost:8608/graphql
NEXT_PUBLIC_MKBJ_API_GRAPHQL_URL=http://localhost:8608/graphql
NEXT_PUBLIC_KEYCLOAK_URL=http://localhost:8081
NEXT_PUBLIC_KEYCLOAK_REALM=Shop
NEXT_PUBLIC_KEYCLOAK_CLIENT_ID=modular-monolith
```

The committed Backoffice and Shop clients are public, use standard flow and PKCE S256, and derive their permitted browser origins from `KC_ADMIN_WEB_ORIGIN` and `KC_SHOP_WEB_ORIGIN`. Development defaults are `http://localhost:5173` and `http://localhost:3000`. Do not add a client secret to frontend configuration.

The host permits all browser origins in Development when `Host:AllowedCorsOrigins` is empty. In a configured or production environment, add the exact admin origin to that array as well as setting the Keycloak origin; these are two independent checks.

## Install and run

```text
pnpm install
pnpm generate:contracts
pnpm dev:admin
pnpm dev:shop
```

Open `http://localhost:5173` for Backoffice or `http://localhost:3000` for the shop. Sign-in is hosted by the corresponding Keycloak realm.

## Browser tests

With the local API, Keycloak, migrations, and seed data running:

```text
pnpm e2e
pnpm e2e:shop
```

The suites use the locally installed Chrome browser, so no Playwright browser download is required. Anonymous checks need no credentials. For authenticated paths, provide `E2E_ADMIN_USERNAME`/`E2E_ADMIN_PASSWORD` or `E2E_SHOP_USERNAME`/`E2E_SHOP_PASSWORD` in the process environment for isolated local users. Never place those values in a committed environment file. Set `E2E_BASE_URL` only when the selected application is served at a different origin.

## Troubleshooting

- An identity-service error means Keycloak is unavailable or its realm/client import did not complete.
- A network/CORS failure usually means the browser origin differs from `KC_ADMIN_WEB_ORIGIN`.
- A database schema error means `--migrate` was not run against the current stack.
- Empty product choices can mean the development data has not been seeded.
- Non-base currencies can become unavailable after `Fx:MaxRateAge`; rerun the development seed (which refreshes or supplies current demo FX rates) and let the storefront views refresh.
- An empty anonymous storefront with a populated admin catalog means the four storefront materialized views need their queued refresh worker to run; purchasing paths never read those views.
- Contract-generation errors mean the checked-in operations no longer match the captured backend schema.
