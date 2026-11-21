'use client';

import { ShopAuthProvider } from '@/auth/shop-auth-provider';

export function Providers({ children }: { children: React.ReactNode }) {
  return <ShopAuthProvider>{children}</ShopAuthProvider>;
}
