import type { Metadata } from 'next';
import { ShopShellDocument } from '@mkbj/api-graphql';
import '@mkbj/design-tokens/shop.css';
import './globals.css';
import { Providers } from './providers';
import { SiteHeader } from '@/components/site-header';
import { shopQuery } from '@/lib/graphql';

export const metadata: Metadata = {
  title: { default: 'MKBJ Shop', template: '%s · MKBJ' },
  description: 'Browse genuine products from MKBJ, a Singapore-based marketplace.',
};

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  let currencies: Array<{ code: string }> = [{ code: 'USD' }];
  let categories: Array<{ id: string; name: string; slug: string }> = [];
  try {
    const shell = await shopQuery(ShopShellDocument, {});
    currencies = shell.currencies;
    categories = shell.shopCategories.map(({ id, name, slug }) => ({ id, name, slug }));
  } catch {
    // The page-level error state remains useful while the API starts; the shell stays navigable.
  }

  return (
    <html data-scroll-behavior="smooth" lang="en">
      <body>
        <Providers>
          <SiteHeader categories={categories} currencies={currencies} />
          <main id="main-content">{children}</main>
          <footer className="site-footer">
            <div className="site-container site-footer__grid">
              <div><span className="shop-wordmark shop-wordmark--footer">MKBJ</span><p>A clear, dependable way to shop from Singapore.</p></div>
              <div><h2>Shop</h2><LinkList links={[['All products', '/products'], ['New arrivals', '/products?sort=NEWEST'], ['Popular', '/products?sort=POPULAR']]} /></div>
              <div><h2>Your account</h2><LinkList links={[['Profile', '/account'], ['Cart', '/cart'], ['Orders', '/orders']]} /></div>
              <div><h2>Information</h2><LinkList links={[['How payment works', '/help#payment'], ['Contact', '/help#contact'], ['Privacy', '/help#privacy']]} /></div>
            </div>
            <div className="site-container site-footer__bottom">© {new Date().getFullYear()} MKBJ. Prices and availability are confirmed in your cart.</div>
          </footer>
        </Providers>
      </body>
    </html>
  );
}

function LinkList({ links }: { links: ReadonlyArray<readonly [string, string]> }) {
  return <ul>{links.map(([label, href]) => <li key={href}><a href={href}>{label}</a></li>)}</ul>;
}
