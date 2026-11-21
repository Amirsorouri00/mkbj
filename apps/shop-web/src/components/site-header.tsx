'use client';

import { Menu, ShoppingBag, UserRound, X } from 'lucide-react';
import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useShopAuth } from '@/auth/shop-auth-provider';
import { CurrencyPicker } from './currency-picker';
import { SiteSearch } from './site-search';

interface NavigationCategory { id: string; name: string; slug: string }

export function SiteHeader({
  currencies,
  categories,
}: {
  currencies: ReadonlyArray<{ code: string }>;
  categories: ReadonlyArray<NavigationCategory>;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [currentCurrency, setCurrentCurrency] = useState('USD');
  const auth = useShopAuth();

  useEffect(() => {
    const currency = new URL(window.location.href).searchParams.get('currency');
    if (currency && /^[A-Z]{3}$/i.test(currency)) setCurrentCurrency(currency.toUpperCase());
  }, []);

  return (
    <>
      <a className="skip-link" href="#main-content">Skip to main content</a>
      <header className="site-header">
        <div className="utility-bar">
          <div className="site-container utility-bar__content">
            <span>Singapore marketplace · Secure account checkout</span>
            <div className="utility-actions">
              <CurrencyPicker currencies={currencies.length ? currencies : [{ code: 'USD' }]} />
              <Link href="/orders" prefetch={false}>Orders</Link>
              <Link href="/help" prefetch={false}>Help</Link>
            </div>
          </div>
        </div>
        <div className="site-container header-main">
          <button
            aria-expanded={menuOpen}
            aria-label={menuOpen ? 'Close navigation' : 'Open navigation'}
            className="icon-button mobile-menu-button"
            onClick={() => setMenuOpen((open) => !open)}
            type="button"
          >
            {menuOpen ? <X aria-hidden="true" /> : <Menu aria-hidden="true" />}
          </button>
          <Link aria-label="MKBJ shop home" className="shop-wordmark" href={`/?currency=${currentCurrency}`} prefetch={false}>MKBJ</Link>
          <SiteSearch currency={currentCurrency} />
          <div className="header-actions">
            {!auth.ready ? (
              <span aria-label="Checking account" className="account-skeleton" />
            ) : auth.authenticated ? (
              <Link className="header-action" href="/account" prefetch={false}>
                <UserRound aria-hidden="true" />
                <span><small>Welcome</small>{auth.displayName ?? 'Your account'}</span>
              </Link>
            ) : (
              <button className="header-action header-action--button" onClick={() => void auth.login()} type="button">
                <UserRound aria-hidden="true" />
                <span><small>Welcome</small>Sign in</span>
              </button>
            )}
            <Link className="header-action cart-action" href="/cart" prefetch={false}>
              <ShoppingBag aria-hidden="true" />
              <span><small>Your</small>Cart</span>
            </Link>
          </div>
        </div>
        <nav aria-label="Shop categories" className={menuOpen ? 'category-nav category-nav--open' : 'category-nav'}>
          <div className="site-container category-nav__inner">
            <Link className="category-nav__featured" href={`/products?currency=${currentCurrency}`} prefetch={false}>Shop all</Link>
            {categories.slice(0, 9).map((category) => (
              <Link href={`/products?currency=${currentCurrency}&categoryId=${encodeURIComponent(category.id)}`} key={category.id} onClick={() => setMenuOpen(false)} prefetch={false}>
                {category.name}
              </Link>
            ))}
          </div>
        </nav>
      </header>
    </>
  );
}
