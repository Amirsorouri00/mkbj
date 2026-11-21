'use client';

import { ShopCartDocument, ShopCheckoutDocument, ShopRemoveCartLineDocument, ShopUpdateCartLineDocument, type ShopCartFieldsFragment } from '@mkbj/api-graphql';
import { Minus, Plus, ShoppingBag, Trash2 } from 'lucide-react';
import Link from 'next/link';
import { useCallback, useEffect, useState } from 'react';
import { useShopAuth } from '@/auth/shop-auth-provider';
import { AuthGate } from '@/components/auth-gate';
import { shopClientRequest } from '@/lib/client-graphql';
import { formatMoney } from '@/lib/money';

export default function CartPage() {
  const auth = useShopAuth();
  return <div className="site-container account-page"><AuthGate returnTo="/cart" title="Sign in to view your cart"><CartContent getToken={auth.getToken} /></AuthGate></div>;
}

function CartContent({ getToken }: { getToken: () => Promise<string | null> }) {
  const [cart, setCart] = useState<ShopCartFieldsFragment | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyVariant, setBusyVariant] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async () => {
    setLoading(true); setError(null);
    const token = await getToken();
    if (!token) { setError('Your session ended. Please sign in again.'); setLoading(false); return; }
    try { setCart((await shopClientRequest(ShopCartDocument, {}, token)).cart); }
    catch (caught) { setError(caught instanceof Error ? caught.message : 'Could not load your cart.'); }
    finally { setLoading(false); }
  }, [getToken]);
  useEffect(() => { void load(); }, [load]);

  const mutateLine = async (variantId: string, quantity: number | null) => {
    setBusyVariant(variantId); setError(null);
    const token = await getToken();
    if (!token) { setError('Your session ended. Please sign in again.'); setBusyVariant(null); return; }
    try {
      if (quantity === null) {
        const result = await shopClientRequest(ShopRemoveCartLineDocument, { input: { variantId } }, token);
        setCart(result.removeCartLine);
      } else {
        const result = await shopClientRequest(ShopUpdateCartLineDocument, { input: { variantId, quantity } }, token);
        setCart(result.updateCartLine);
      }
    } catch (caught) { setError(caught instanceof Error ? caught.message : 'Could not update your cart.'); }
    finally { setBusyVariant(null); }
  };

  const checkout = async () => {
    if (!cart) return;
    setBusyVariant('checkout'); setError(null);
    const token = await getToken();
    if (!token) { setError('Your session ended. Please sign in again.'); setBusyVariant(null); return; }
    try {
      const { checkout: outcome } = await shopClientRequest(ShopCheckoutDocument, { input: { expectedTotal: cart.total } }, token);
      if (outcome.priceChanged) { setCart(outcome.priceChanged); setError('A price changed. Review the refreshed cart total before checking out again.'); }
      else if (outcome.order) window.location.assign(`/orders/${outcome.order.id}`);
      else setError(outcome.code ?? 'Checkout could not be completed.');
    } catch (caught) { setError(caught instanceof Error ? caught.message : 'Checkout could not be completed.'); }
    finally { setBusyVariant(null); }
  };

  if (loading) return <div className="account-state" role="status">Loading your cart…</div>;
  if (!cart) return <div className="account-state"><h1>Your cart is unavailable</h1>{error && <p role="alert">{error}</p>}<button className="button button--secondary" onClick={() => void load()} type="button">Try again</button></div>;
  return (
    <div className="cart-page">
      <header className="account-heading"><p className="eyebrow">Your basket</p><h1>Shopping cart</h1><p>Live prices are checked whenever the cart changes.</p></header>
      {cart.changes.length > 0 && <div className="price-change-notice" role="status"><strong>Your cart was refreshed.</strong><span>Review the latest prices before checkout.</span></div>}
      {error && <div className="inline-error" role="alert">{error}</div>}
      {!cart.lines.length ? <section className="empty-cart"><ShoppingBag aria-hidden="true" /><h2>Your cart is empty</h2><p>Find something useful and it will appear here.</p><Link className="button button--primary" href="/products" prefetch={false}>Browse products</Link></section> : (
        <div className="cart-layout"><section aria-label="Cart items" className="cart-lines">{cart.lines.map((line) => (
          <article className="cart-line" key={line.variantId}><div><p className="product-card__brand">{line.sku}</p><h2>{line.productTitle}</h2><p>{formatMoney(line.unitPrice)} each</p></div><div className="quantity-stepper" aria-label={`Quantity for ${line.productTitle}`}><button aria-label="Decrease quantity" disabled={busyVariant === line.variantId || line.quantity <= 1} onClick={() => void mutateLine(line.variantId, line.quantity - 1)} type="button"><Minus aria-hidden="true" /></button><span>{line.quantity}</span><button aria-label="Increase quantity" disabled={busyVariant === line.variantId} onClick={() => void mutateLine(line.variantId, line.quantity + 1)} type="button"><Plus aria-hidden="true" /></button></div><strong>{formatMoney(line.lineTotal)}</strong><button aria-label={`Remove ${line.productTitle}`} className="icon-button danger-button" disabled={busyVariant === line.variantId} onClick={() => void mutateLine(line.variantId, null)} type="button"><Trash2 aria-hidden="true" /></button></article>
        ))}</section><aside className="order-summary"><h2>Order summary</h2><div><span>Items</span><strong>{cart.lines.reduce((total, line) => total + line.quantity, 0)}</strong></div><div className="order-summary__total"><span>Total</span><strong>{formatMoney(cart.total)}</strong></div><p>Payment is made externally after checkout. Your order will remain pending until payment is confirmed.</p><button className="button button--primary button--full" disabled={busyVariant === 'checkout'} onClick={() => void checkout()} type="button">{busyVariant === 'checkout' ? 'Placing order…' : 'Continue to checkout'}</button></aside></div>
      )}
    </div>
  );
}
