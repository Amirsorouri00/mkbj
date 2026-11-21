'use client';

import { LockKeyhole } from 'lucide-react';
import { useShopAuth } from '@/auth/shop-auth-provider';

export function AuthGate({ children, title, returnTo }: { children: React.ReactNode; title: string; returnTo: string }) {
  const auth = useShopAuth();
  if (!auth.ready) return <div className="account-state" role="status">Checking your account…</div>;
  if (auth.authenticated) return children;
  return (
    <section className="account-state">
      <LockKeyhole aria-hidden="true" /><p className="eyebrow">Your MKBJ account</p><h1>{title}</h1>
      <p>Sign in securely through MKBJ to continue. Your password is never handled by the shop application.</p>
      <div className="hero-actions"><button className="button button--primary" onClick={() => void auth.login(returnTo)} type="button">Sign in</button><button className="button button--secondary" onClick={() => void auth.register(returnTo)} type="button">Create an account</button></div>
    </section>
  );
}
