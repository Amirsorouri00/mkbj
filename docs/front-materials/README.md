# Frontend Developer Documentation — EwaysVNext

This directory contains everything a frontend team needs to integrate with the EwaysVNext API.

## Documents

| File | Audience | Contents |
|------|----------|----------|
| [getting-started.md](getting-started.md) | All | Base URLs, authentication setup, Scalar, error format |
| [shop-api.md](shop-api.md) | Shop frontend | Anonymous storefront + authenticated customer endpoints |
| [admin-api.md](admin-api.md) | Admin panel | Backoffice staff/super endpoints across all modules |
| [partner-api.md](partner-api.md) | Partner integrations | API-key-authenticated B2B endpoints |
| [graphql-api.md](graphql-api.md) | All | GraphQL schema, auth per resolver, Banana Cake Pop |

## Quick Start

```bash
# Start the dev stack (Postgres, MinIO, Keycloak)
docker compose -f build/compose/docker-compose.dev.yml up -d

# Run the API
dotnet run --project src/Host/EwaysVNext.Host

# Apply migrations + seed demo data
dotnet run --project src/Host/EwaysVNext.Host -- --migrate
dotnet run --project src/Host/EwaysVNext.Host -- --seed
```

Then open:

- **Scalar (REST)**: http://localhost:5000/scalar/v1
- **GraphQL (Banana Cake Pop)**: http://localhost:5000/graphql
- **OpenAPI spec**: http://localhost:5000/openapi/v1.json
