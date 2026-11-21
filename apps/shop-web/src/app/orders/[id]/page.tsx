'use client';

import { ShopDocumentPdfUrlDocument, ShopOrderDocument, type ShopOrderFieldsFragment } from '@mkbj/api-graphql';
import { Download, FileText } from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';
import { useShopAuth } from '@/auth/shop-auth-provider';
import { AuthGate } from '@/components/auth-gate';
import { shopClientRequest } from '@/lib/client-graphql';
import { formatMinor } from '@/lib/money';

export default function OrderPage() {
  const params = useParams<{ id: string }>();
  const auth = useShopAuth();
  return <div className="site-container account-page"><AuthGate returnTo={`/orders/${params.id}`} title="Sign in to view this order"><OrderDetail getToken={auth.getToken} id={params.id} /></AuthGate></div>;
}

function OrderDetail({ id, getToken }: { id: string; getToken: () => Promise<string | null> }) {
  const [order, setOrder] = useState<ShopOrderFieldsFragment | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async () => { const token = await getToken(); if (!token) return; try { setOrder((await shopClientRequest(ShopOrderDocument, { id }, token)).order); } catch (caught) { setError(caught instanceof Error ? caught.message : 'Could not load this order.'); } }, [getToken, id]);
  useEffect(() => { void load(); }, [load]);
  const download = async (documentId: string) => { const token = await getToken(); if (!token) return; try { const { documentPdfUrl } = await shopClientRequest(ShopDocumentPdfUrlDocument, { documentId }, token); window.open(documentPdfUrl, '_blank', 'noopener,noreferrer'); } catch (caught) { setError(caught instanceof Error ? caught.message : 'Could not open this document.'); } };
  if (!order && !error) return <div className="account-state" role="status">Loading order…</div>;
  if (!order) return <div className="account-state"><h1>Order unavailable</h1><p role="alert">{error}</p><Link className="button button--secondary" href="/orders" prefetch={false}>Back to orders</Link></div>;
  return <div className="order-detail"><nav aria-label="Breadcrumb" className="breadcrumbs"><Link href="/orders" prefetch={false}>Orders</Link><span aria-hidden="true">/</span><span aria-current="page">{order.number}</span></nav><header className="account-heading"><div><p className="eyebrow">Order {order.number}</p><h1>{order.status.order.replaceAll('_', ' ')}</h1><p>Placed {new Intl.DateTimeFormat('en-SG', { dateStyle: 'long', timeStyle: 'short' }).format(new Date(order.createdAt))}</p></div><strong className="order-total">{formatMinor(order.totalMinor, order.currency)}</strong></header>{error && <div className="inline-error" role="alert">{error}</div>}
    <div className="order-detail-grid"><section className="order-detail-panel"><h2>Items</h2>{order.lines.map((line) => <article className="order-detail-line" key={line.lineNo}><div><strong>{line.productTitle}</strong><span>{line.brandName} · {line.sku}</span><small>Quantity {line.quantity}</small></div><strong>{formatMinor(line.lineTotalMinor, order.currency)}</strong></article>)}</section><aside className="order-detail-panel"><h2>Payment and fulfillment</h2><dl className="status-list"><div><dt>Payment</dt><dd>{order.status.payment.replaceAll('_', ' ')}</dd></div><div><dt>Fulfillment</dt><dd>{order.status.fulfillment.replaceAll('_', ' ')}</dd></div><div><dt>Documents</dt><dd>{order.status.documents.replaceAll('_', ' ')}</dd></div></dl>{order.status.payment === 'PENDING' && <p className="payment-note">Follow the payment instructions in your pro forma document. MKBJ staff will confirm receipt independently.</p>}</aside></div>
    <section className="documents-panel"><h2>Documents</h2>{order.documents.length ? <div>{order.documents.map((document) => <button className="document-row" key={document.id} onClick={() => void download(document.id)} type="button"><FileText aria-hidden="true" /><span><strong>{document.type.replaceAll('_', ' ')}</strong><small>{document.number}</small></span><Download aria-hidden="true" /></button>)}</div> : <p>Documents will appear here when they are issued.</p>}</section>
  </div>;
}
