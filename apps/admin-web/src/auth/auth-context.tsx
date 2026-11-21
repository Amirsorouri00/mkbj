import Keycloak, { type KeycloakProfile } from 'keycloak-js';
import { createContext, type PropsWithChildren, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { appConfig } from '../config';

interface AuthState {
  ready: boolean;
  authenticated: boolean;
  token: string | null;
  profile: KeycloakProfile | null;
  roles: ReadonlyArray<string>;
  initializationError: string | null;
  login: () => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

const keycloak = new Keycloak({
  url: appConfig.keycloakUrl,
  realm: appConfig.keycloakRealm,
  clientId: appConfig.keycloakClientId,
});
let initialization: Promise<boolean> | undefined;

function profileFromToken(): KeycloakProfile | null {
  const claims = keycloak.tokenParsed;
  if (!claims) return null;
  return {
    ...(typeof claims.preferred_username === 'string' ? { username: claims.preferred_username } : {}),
    ...(typeof claims.given_name === 'string' ? { firstName: claims.given_name } : {}),
    ...(typeof claims.family_name === 'string' ? { lastName: claims.family_name } : {}),
    ...(typeof claims.email === 'string' ? { email: claims.email } : {}),
  };
}

function initializeKeycloak(): Promise<boolean> {
  initialization ??= keycloak.init({
    onLoad: 'check-sso',
    pkceMethod: 'S256',
    checkLoginIframe: true,
    silentCheckSsoRedirectUri: `${window.location.origin}/silent-check-sso.html`,
  });
  return initialization;
}

export function AuthProvider({ children }: PropsWithChildren) {
  const [ready, setReady] = useState(false);
  const [authenticated, setAuthenticated] = useState(false);
  const [token, setToken] = useState<string | null>(null);
  const [profile, setProfile] = useState<KeycloakProfile | null>(null);
  const [roles, setRoles] = useState<ReadonlyArray<string>>([]);
  const [initializationError, setInitializationError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    const syncToken = () => {
      if (!active) return;
      setAuthenticated(Boolean(keycloak.authenticated));
      setToken(keycloak.token ?? null);
      setRoles(keycloak.realmAccess?.roles ?? []);
    };

    keycloak.onAuthSuccess = syncToken;
    keycloak.onAuthRefreshSuccess = syncToken;
    keycloak.onAuthLogout = syncToken;
    keycloak.onTokenExpired = () => {
      void keycloak.updateToken(30).then(syncToken).catch(() => {
        void keycloak.clearToken();
        syncToken();
      });
    };

    void initializeKeycloak()
      .then((isAuthenticated) => {
        if (!active) return;
        setAuthenticated(isAuthenticated);
        setToken(keycloak.token ?? null);
        setRoles(keycloak.realmAccess?.roles ?? []);
        if (isAuthenticated) {
          setProfile(profileFromToken());
        }
      })
      .catch(() => active && setInitializationError('The identity service is unavailable. Check the connection and try again.'))
      .finally(() => active && setReady(true));

    return () => {
      active = false;
    };
  }, []);

  const login = useCallback(
    () => keycloak.login({ redirectUri: window.location.href }),
    [],
  );
  const logout = useCallback(
    () => keycloak.logout({ redirectUri: window.location.origin }),
    [],
  );

  const value = useMemo(
    () => ({ ready, authenticated, token, profile, roles, initializationError, login, logout }),
    [authenticated, initializationError, login, logout, profile, ready, roles, token],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  const auth = useContext(AuthContext);
  if (!auth) throw new Error('useAuth must be used within AuthProvider');
  return auth;
}
