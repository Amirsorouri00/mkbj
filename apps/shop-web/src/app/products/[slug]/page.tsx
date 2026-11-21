import { ShopProductDocument } from '@mkbj/api-graphql';
import { ChevronRight, ImageIcon, ShieldCheck } from 'lucide-react';
import type { Metadata } from 'next';
import Image from 'next/image';
import Link from 'next/link';
import { notFound } from 'next/navigation';
import { AddToCart } from '@/components/add-to-cart';
import { ShopError } from '@/components/shop-error';
import { shopQuery, ShopApiError } from '@/lib/graphql';

export const dynamic = 'force-dynamic';

type Props = { params: Promise<{ slug: string }>; searchParams: Promise<{ currency?: string }> };

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  return { title: slug.split('-').map((part) => part.charAt(0).toUpperCase() + part.slice(1)).join(' ') };
}

export default async function ProductPage({ params, searchParams }: Props) {
  const { slug } = await params;
  const { currency: rawCurrency } = await searchParams;
  const currency = /^[A-Z]{3}$/i.test(rawCurrency ?? '') ? rawCurrency!.toUpperCase() : 'USD';
  try {
    const { shopProduct: product } = await shopQuery(ShopProductDocument, { slug, currency });
    return (
      <div className="site-container product-page">
        <nav aria-label="Breadcrumb" className="breadcrumbs"><Link href="/" prefetch={false}>Home</Link><ChevronRight aria-hidden="true" />{product.breadcrumb.map((crumb) => <span key={crumb.categoryId}><Link href={`/products?currency=${currency}&categoryId=${crumb.categoryId}`} prefetch={false}>{crumb.name}</Link><ChevronRight aria-hidden="true" /></span>)}<span aria-current="page">{product.title}</span></nav>
        <div className="product-detail-grid">
          <section aria-label="Product images" className="product-gallery">
            {product.imageUrls.length ? product.imageUrls.map((url, index) => <div className={index === 0 ? 'gallery-image gallery-image--primary' : 'gallery-image'} key={url}><Image alt={index === 0 ? product.title : `${product.title}, view ${index + 1}`} height={900} priority={index === 0} sizes="(max-width: 900px) 100vw, 55vw" src={url} unoptimized width={900} /></div>) : <div className="gallery-empty"><ImageIcon aria-hidden="true" /><span>Image coming soon</span></div>}
          </section>
          <section className="product-summary">
            <p className="product-summary__brand">{product.brandName}</p><h1>{product.title}</h1>
            {product.shortDescription && <p className="product-summary__short">{product.shortDescription}</p>}
            <AddToCart returnTo={`/products/${product.slug}?currency=${currency}`} variants={product.variants} />
            <div className="assurance-list"><span><ShieldCheck aria-hidden="true" /><strong>Live pricing</strong><small>Your cart rechecks the current price.</small></span><span><ShieldCheck aria-hidden="true" /><strong>Clear documents</strong><small>Pro forma and commercial documents stay in your account.</small></span></div>
          </section>
        </div>
        <section className="product-description"><p className="eyebrow">Product details</p><h2>About this product</h2><p>{product.description ?? product.shortDescription ?? 'More information will be added soon.'}</p>{product.attributeOptions.length > 0 && <dl>{product.attributeOptions.map(({ key, value }) => <div key={key}><dt>{key}</dt><dd>{value.join(', ')}</dd></div>)}</dl>}</section>
      </div>
    );
  } catch (error) {
    if (error instanceof ShopApiError && error.code === 'NOT_FOUND') notFound();
    return <div className="site-container page-space"><ShopError message={error instanceof Error ? error.message : 'Please try again shortly.'} title="This product could not be loaded" /></div>;
  }
}
