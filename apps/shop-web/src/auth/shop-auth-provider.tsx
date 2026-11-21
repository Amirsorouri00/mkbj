'use client';

import Keycloak from 'keycloak-js';
import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { publicConfig } from '@/lib/config';

interface ShopAuthValue {
  ready: boolean;
  authenticated: boolean;
  displayName: string | null;
  login: (returnTo?: string) => Promise<void>;
  register: (returnTo?: string) => Promise<void>;
  logout: () => Promise<void>;
  getToken: () => Promise<string | null>;
}

const ShopAuthContext = createContext<ShopAuthValue | null>(null);

let client: Keycloak | null = null;
function getClient(): Keycloak {
  client ??= new Keycloak({
    url: publicConfig.keycloakUrl,
    realm: publicConfig.keycloakRealm,
    clientId: publicConfig.keycloakClientId,
  });
  return client;
}

export function ShopAuthProvider({ children }: { children: React.ReactNode }) {
  const [ready, setReady] = useState(false);
  const [authenticated, setAuthenticated] = useState(false);
  const [displayName, setDisplayName] = useState<string | null>(null);

  useEffect(() => {
    const keycloak = getClient();
    let active = true;
    keycloak.onAuthLogout = () => {
      if (active) setAuthenticated(false);
    };
    keycloak.onTokenExpired = () => void keycloak.updateToken(30);

    void keycloak.init({
      onLoad: 'check-sso',
      pkceMethod: 'S256',
      checkLoginIframe: false,
      silentCheckSsoRedirectUri: `${window.location.origin}/silent-check-sso.html`,
    }).then((isAuthenticated) => {
      if (!active) return;
      setAuthenticated(isAuthenticated);
      const claims = keycloak.tokenParsed;
      setDisplayName(
        typeof claims?.name === 'string'
          ? claims.name
          : typeof claims?.preferred_username === 'string'
            ? claims.preferred_username
            : null,
      );
      setReady(true);
    }).catch(() => {
      if (active) setReady(true);
    });

    return () => { active = false; };
  }, []);

  const destination = useCallback((returnTo?: string) => {
    const path = returnTo ?? `${window.location.pathname}${window.location.search}`;
    return new URL(path, window.location.origin).toString();
  }, []);

  const login = useCallback(async (returnTo?: string) => {
    await getClient().login({ redirectUri: destination(returnTo) });
  }, [destination]);
  const register = useCallback(async (returnTo?: string) => {
    await getClient().register({ redirectUri: destination(returnTo) });
  }, [destination]);
  const logout = useCallback(async () => {
    await getClient().logout({ redirectUri: window.location.origin });
  }, []);
  const getToken = useCallback(async () => {
    const keycloak = getClient();
    if (!keycloak.authenticated) return null;
    await keycloak.updateToken(30);
    return keycloak.token ?? null;
  }, []);

  const value = useMemo(
    () => ({ ready, authenticated, displayName, login, register, logout, getToken }),
    [ready, authenticated, displayName, login, register, logout, getToken],
  );
  return <ShopAuthContext.Provider value={value}>{children}</ShopAuthContext.Provider>;
}

export function useShopAuth(): ShopAuthValue {
  const value = useContext(ShopAuthContext);
  if (!value) throw new Error('useShopAuth must be used within ShopAuthProvider.');
  return value;
}
