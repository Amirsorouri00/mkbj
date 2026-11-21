# Admin Frontend Architecture

## Scope

`apps/admin-web` is the React/Vite single-page application for Backoffice staff. The implemented customer storefront lives in `apps/shop-web` with independent design tokens and composition; see `SHOP_ARCHITECTURE.md`.

The .NET modular monolith remains the only backend and the authority for authorization, validation, persistence and business decisions.

## Workspace boundaries

```text
apps/admin-web          Admin routes, feature UI and orchestration
packages/api-graphql   Checked-in SDL, operations and generated TypeScript
packages/api-rest      Checked-in OpenAPI and generated TypeScript
packages/design-tokens Admin semantic theme variables
packages/ui            Theme-neutral primitives
packages/config        Shared TypeScript configuration
```

Admin business components remain in `apps/admin-web`; shared packages do not depend on an application.

## Runtime composition

The application starts with a TanStack Query client and a Keycloak authentication provider. Keycloak uses Authorization Code flow with PKCE S256. Tokens remain in the adapter's memory and session restoration uses Keycloak's silent SSO page.

Authenticated routes render inside the responsive shell. Catalog Products is the default landing page. Feature routes are loaded lazily so the editor and detail dependencies do not inflate the initial sign-in bundle.

## Data flow

1. A route parses normalized filter and pagination state from the URL.
2. A feature query builds a stable key containing the complete effective server request.
3. The typed transport sends the generated GraphQL document or the verified REST upload.
4. GraphQL and REST failures become one `AppError` shape.
5. Successful mutations update the affected detail and invalidate the product list.
6. Backend validation and activation refusals are shown without replacing them with frontend business decisions.

Catalog and Pricing remain separate. The catalog product object is never treated as a price or proof that an item is sellable.

## Security boundary

- Backoffice realm only; no Shop token or partner key path exists in the admin app.
- No client secret is present or supported.
- Snowflake IDs remain opaque strings.
- File uploads go through the authenticated API, never directly to object storage.
- Browser authorization improves navigation UX, while every operation remains backend-enforced.
