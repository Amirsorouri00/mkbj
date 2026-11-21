import Link from 'next/link';

export default function NotFound() {
  return <section className="site-container not-found"><p className="eyebrow">404</p><h1>We could not find that page.</h1><p>The product may have moved or is no longer available.</p><Link className="button button--primary" href="/products" prefetch={false}>Browse products</Link></section>;
}
