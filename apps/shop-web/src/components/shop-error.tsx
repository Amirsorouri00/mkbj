import { AlertCircle } from 'lucide-react';
import Link from 'next/link';

export function ShopError({ title = 'We could not load this section', message }: { title?: string; message: string }) {
  return (
    <section className="shop-error" role="alert">
      <AlertCircle aria-hidden="true" />
      <div><h2>{title}</h2><p>{message}</p><Link className="button button--secondary" href="/" prefetch={false}>Return home</Link></div>
    </section>
  );
}
