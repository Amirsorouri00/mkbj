'use client';

import { ShopOrdersDocument, type ShopOrderFieldsFragment } from '@mkbj/api-graphql';
import { ChevronRight, PackageCheck } from 'lucide-react';
import Link from 'next/link';
import { useCallback, useEffect, useState } from 'react';
import { useShopAuth } from '@/auth/shop-auth-provider';
import { AuthGate } from '@/components/auth-gate';
import { shopClientRequest } from '@/lib/client-graphql';
import { formatMinor } from '@/lib/money';

export default function OrdersPage() {
  const auth = useShopAuth();
  return <div className="site-container account-page"><AuthGate returnTo="/orders" title="Sign in to view your orders"><OrderList getToken={auth.getToken} /></AuthGate></div>;
}

function OrderList({ getToken }: { getToken: () => Promise<string | null> }) {
  const [orders, setOrders] = useState<ShopOrderFieldsFragment[]>([]);
  const [nextCursor, setNextCursor] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async (cursor?: string) => { setLoading(true); const token = await getToken(); if (!token) return; try { const page = (await shopClientRequest(ShopOrdersDocument, { cursor: cursor ?? null, limit: 20 }, token)).myOrders; setOrders((current) => cursor ? [...current, ...page.items] : page.items); setNextCursor(page.nextCursor ?? null); } catch (caught) { setError(caught instanceof Error ? caught.message : 'Could not load your orders.'); } finally { setLoading(false); } }, [getToken]);
  useEffect(() => { void load(); }, [load]);
  return <div className="orders-page"><header className="account-heading"><p className="eyebrow">Purchase history</p><h1>Your orders</h1><p>Track pending payments and access issued documents.</p></header>{error && <div className="inline-error" role="alert">{error}</div>}{!loading && !orders.length ? <section className="empty-cart"><PackageCheck aria-hidden="true" /><h2>No orders yet</h2><p>Your completed checkouts will appear here.</p><Link className="button button--primary" href="/products" prefetch={false}>Start shopping</Link></section> : <div className="order-list">{orders.map((order) => <Link className="order-card" href={`/orders/${order.id}`} key={order.id} prefetch={false}><div><span className="status-pill">{order.status.order.replaceAll('_', ' ')}</span><h2>Order {order.number}</h2><p>{new Intl.DateTimeFormat('en-SG', { dateStyle: 'medium' }).format(new Date(order.createdAt))} · {order.lines.length} items</p></div><strong>{formatMinor(order.totalMinor, order.currency)}</strong><ChevronRight aria-hidden="true" /></Link>)}</div>}{loading && <p className="loading-more" role="status">Loading orders…</p>}{nextCursor && !loading && <button className="button button--secondary" onClick={() => void load(nextCursor)} type="button">Load more</button>}</div>;
}
