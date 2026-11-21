export interface AppConfig {
  apiBaseUrl: string;
  graphqlUrl: string;
  keycloakUrl: string;
  keycloakRealm: string;
  keycloakClientId: string;
}

function value(raw: string | undefined, name: string, fallback?: string): string {
  const resolved = raw || fallback;
  if (!resolved) throw new Error(`Missing required environment setting: ${name}`);
  return resolved.replace(/\/$/, '');
}

export const appConfig: AppConfig = {
  apiBaseUrl: value(import.meta.env.VITE_API_BASE_URL, 'VITE_API_BASE_URL', 'http://localhost:8608'),
  graphqlUrl: value(import.meta.env.VITE_GRAPHQL_URL, 'VITE_GRAPHQL_URL', 'http://localhost:8608/graphql'),
  keycloakUrl: value(import.meta.env.VITE_KEYCLOAK_URL, 'VITE_KEYCLOAK_URL', 'http://localhost:8081'),
  keycloakRealm: value(import.meta.env.VITE_KEYCLOAK_REALM, 'VITE_KEYCLOAK_REALM', 'Backoffice'),
  keycloakClientId: value(import.meta.env.VITE_KEYCLOAK_CLIENT_ID, 'VITE_KEYCLOAK_CLIENT_ID', 'backoffice-console'),
};
