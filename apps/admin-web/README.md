# MKBJ Admin Web

The Admin Backoffice contains Keycloak sign-in, the responsive application shell, complete supported Catalog management workflows, per-variant Pricing integration, explicit-ID bulk editing, and duplicate merge confirmation.

## Run locally

1. Copy `.env.example` to `.env.local` and adjust the API and Keycloak URLs.
2. Configure the Backoffice Keycloak client for Authorization Code flow, PKCE S256, the Vite origin and `/silent-check-sso.html` redirect.
3. From the repository root run `pnpm install` and then `pnpm dev`.

The access token is held in memory only. On refresh, the app restores the session through Keycloak's silent SSO page; it does not write tokens to local or session storage.

## Quality commands

```text
pnpm generate:contracts
pnpm check:contracts
pnpm typecheck
pnpm lint
pnpm test
pnpm build
pnpm e2e
```

Contract generation uses the checked-in SDL and OpenAPI snapshots, so it does not require the backend to be running. Refresh the snapshots from a running backend whenever its public contract changes.

Playwright uses the locally installed Chrome browser. The authentication-entry smoke check needs Keycloak; authenticated Catalog scenarios additionally need the running API plus `E2E_ADMIN_USERNAME` and `E2E_ADMIN_PASSWORD` for an isolated Backoffice test user. Those values must never be committed.
