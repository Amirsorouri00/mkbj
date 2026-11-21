const trim = (value: string) => value.replace(/\/$/, '');

export const serverConfig = {
  graphqlUrl: trim(process.env.MKBJ_API_GRAPHQL_URL ?? 'http://localhost:8608/graphql'),
};

export const publicConfig = {
  graphqlUrl: trim(process.env.NEXT_PUBLIC_MKBJ_API_GRAPHQL_URL ?? 'http://localhost:8608/graphql'),
  keycloakUrl: trim(process.env.NEXT_PUBLIC_KEYCLOAK_URL ?? 'http://localhost:8081'),
  keycloakRealm: process.env.NEXT_PUBLIC_KEYCLOAK_REALM ?? 'Shop',
  keycloakClientId: process.env.NEXT_PUBLIC_KEYCLOAK_CLIENT_ID ?? 'modular-monolith',
};
