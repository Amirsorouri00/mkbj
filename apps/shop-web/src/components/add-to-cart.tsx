'use client';

import { ShopAddToCartDocument, type ShopProductQuery } from '@mkbj/api-graphql';
import { Check, ShoppingBag } from 'lucide-react';
import Link from 'next/link';
import { useMemo, useState, useTransition } from 'react';
import { useShopAuth } from '@/auth/shop-auth-provider';
import { shopClientRequest } from '@/lib/client-graphql';
import { formatMoney } from '@/lib/money';

type Variant = ShopProductQuery['shopProduct']['variants'][number];

export function AddToCart({ variants, returnTo }: { variants: ReadonlyArray<Variant>; returnTo: string }) {
  const [variantId, setVariantId] = useState(variants[0]?.variantId ?? '');
  const [quantity, setQuantity] = useState(1);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [pending, startTransition] = useTransition();
  const auth = useShopAuth();
  const selected = useMemo(() => variants.find((variant) => variant.variantId === variantId), [variants, variantId]);

  if (!selected) return <div className="unavailable-box">This product has no priced options available.</div>;
  return (
    <div className="purchase-box">
      <p className="purchase-price">{formatMoney(selected.price)}</p>
      {variants.length > 1 && <label className="field-label">Choose an option
        <select onChange={(event) => setVariantId(event.target.value)} value={variantId}>
          {variants.map((variant) => <option key={variant.variantId} value={variant.variantId}>{variant.attributes.length ? variant.attributes.map(({ key, value }) => `${key}: ${value}`).join(' · ') : variant.sku}</option>)}
        </select>
      </label>}
      <label className="field-label quantity-field">Quantity<input min={1} onChange={(event) => setQuantity(Math.max(1, Number(event.target.value) || 1))} type="number" value={quantity} /></label>
      {!auth.ready ? <button className="button button--primary button--full" disabled>Checking your account…</button> : !auth.authenticated ? (
        <div className="sign-in-prompt"><button className="button button--primary button--full" onClick={() => void auth.login(returnTo)} type="button"><ShoppingBag aria-hidden="true" /> Sign in to add to cart</button><button className="text-button" onClick={() => void auth.register(returnTo)} type="button">New to MKBJ? Create an account</button></div>
      ) : (
        <button className="button button--primary button--full" disabled={pending} onClick={() => startTransition(async () => {
          setError(null); setMessage(null);
          const token = await auth.getToken();
          if (!token) { setError('Your session ended. Please sign in again.'); return; }
          try {
            await shopClientRequest(ShopAddToCartDocument, { input: { variantId, quantity } }, token);
            setMessage('Added to your cart.');
          } catch (caught) { setError(caught instanceof Error ? caught.message : 'Could not add this item.'); }
        })} type="button"><ShoppingBag aria-hidden="true" />{pending ? 'Adding…' : 'Add to cart'}</button>
      )}
      {message && <p className="success-message" role="status"><Check aria-hidden="true" />{message} <Link href="/cart" prefetch={false}>View cart</Link></p>}
      {error && <p className="field-error" role="alert">{error}</p>}
      <p className="purchase-note">Price and availability are confirmed from live data when added to your cart.</p>
    </div>
  );
}
