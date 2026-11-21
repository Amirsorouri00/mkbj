import {
  AdminPricingCurrenciesDocument,
  type AdminPricingCurrenciesQuery,
  type AdminPricingCurrenciesQueryVariables,
  AdminVariantPricingDocument,
  type AdminVariantPricingQuery,
  type AdminVariantPricingQueryVariables,
  RemoveAdminCurrencyOverrideDocument,
  SetAdminCurrencyOverrideDocument,
  SetAdminVariantPriceDocument,
  type ProductDetailFieldsFragment,
} from '@mkbj/api-graphql';
import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { type FormEvent, useEffect, useState } from 'react';
import { graphQlRequest } from '../../api/graphql';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ErrorState } from '../../components/feedback';
import { formatMinorMoney, isMoneyText, minorToAmount } from './money';

type Variant = ProductDetailFieldsFragment['variants'][number];

export function ProductPricingSection({ variants }: { variants: ProductDetailFieldsFragment['variants'] }) {
  const { token } = useAuth();
  const queryClient = useQueryClient();
  const [variantId, setVariantId] = useState(variants[0]?.id ?? '');
  const [baseAmount, setBaseAmount] = useState('');
  const [baseCurrency, setBaseCurrency] = useState('SGD');
  const [overrideAmount, setOverrideAmount] = useState('');
  const [overrideCurrency, setOverrideCurrency] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    if (!variants.some((variant) => variant.id === variantId)) setVariantId(variants[0]?.id ?? '');
  }, [variantId, variants]);

  const currencies = useQuery({ queryKey: ['admin-pricing-currencies'], queryFn: ({ signal }) => graphQlRequest<AdminPricingCurrenciesQuery, AdminPricingCurrenciesQueryVariables>(AdminPricingCurrenciesDocument, {}, token!, signal), enabled: Boolean(token), staleTime: 5 * 60_000 });
  const price = useQuery({ queryKey: ['admin-variant-price', variantId], queryFn: ({ signal }) => graphQlRequest<AdminVariantPricingQuery, AdminVariantPricingQueryVariables>(AdminVariantPricingDocument, { variantId }, token!, signal), enabled: Boolean(token && variantId), retry: false });
  const missing = price.isError && toAppError(price.error).code === 'PRICE_MISSING';
  useEffect(() => {
    if (price.data) {
      setBaseAmount(minorToAmount(price.data.adminVariantPrice.baseAmountMinor));
      setBaseCurrency(price.data.adminVariantPrice.baseCurrency);
    } else if (missing) setBaseAmount('');
  }, [missing, price.data]);
  useEffect(() => {
    const enabled = currencies.data?.adminCurrencies.filter((currency) => currency.isEnabled) ?? [];
    if (!enabled.some((currency) => currency.code === baseCurrency) && enabled[0]) setBaseCurrency(enabled[0].code);
    if (!overrideCurrency && enabled[0]) setOverrideCurrency(enabled[0].code);
  }, [baseCurrency, currencies.data, overrideCurrency]);
  const refreshed = async (message: string) => { await queryClient.invalidateQueries({ queryKey: ['admin-variant-price', variantId] }); setNotice(message); setError(null); };
  const setBase = useMutation({ mutationFn: () => graphQlRequest(SetAdminVariantPriceDocument, { input: { variantId, amount: baseAmount, currency: baseCurrency } }, token!), onSuccess: () => refreshed('Reference price saved.'), onError: (caught) => setError(toAppError(caught).message) });
  const setOverride = useMutation({ mutationFn: () => graphQlRequest(SetAdminCurrencyOverrideDocument, { input: { variantId, amount: overrideAmount, currency: overrideCurrency } }, token!), onSuccess: async () => { await refreshed('Currency override saved.'); setOverrideAmount(''); }, onError: (caught) => setError(toAppError(caught).message) });
  const removeOverride = useMutation({ mutationFn: (currency: string) => graphQlRequest(RemoveAdminCurrencyOverrideDocument, { input: { variantId, currency } }, token!), onSuccess: () => refreshed('Currency override removed.'), onError: (caught) => setError(toAppError(caught).message) });
  const submitBase = (event: FormEvent) => { event.preventDefault(); setError(null); if (!isMoneyText(baseAmount)) return setError('Use a non-negative amount with exactly two decimal places, for example 100.00.'); setBase.mutate(); };
  const submitOverride = (event: FormEvent) => { event.preventDefault(); setError(null); if (!isMoneyText(overrideAmount)) return setError('Use a non-negative amount with exactly two decimal places, for example 100.00.'); if (overrideCurrency === price.data?.adminVariantPrice.baseCurrency) return setError('Choose a currency different from the reference price.'); setOverride.mutate(); };
  const enabledCurrencies = currencies.data?.adminCurrencies.filter((currency) => currency.isEnabled) ?? [];
  const selectedVariant: Variant | undefined = variants.find((variant) => variant.id === variantId);

  return (
    <section className="panel detail-section pricing-section"><div className="section-heading"><div><h2>Pricing</h2><span>Pricing module</span></div></div><p className="muted">Prices are loaded and saved independently from Catalog. Product activation does not imply that a variant is priced.</p>
      {!variants.length ? <p className="muted">Add a variant before setting prices.</p> : <><label className="field"><span>Variant</span><select onChange={(event) => { setVariantId(event.target.value); setError(null); setNotice(null); }} value={variantId}>{variants.map((variant) => <option key={variant.id} value={variant.id}>{variant.sku || `Variant ${variant.id}`} · {variant.isActive ? 'Active' : 'Inactive'}</option>)}</select></label>
      {notice && <div className="success-panel" role="status"><span>{notice}</span><Button onClick={() => setNotice(null)} size="compact" variant="ghost">Dismiss</Button></div>}
      {error && <div className="dialog-error" role="alert">{error}</div>}
      {price.isPending || currencies.isPending ? <p className="muted">Loading price…</p> : currencies.isError ? <ErrorState error={currencies.error} retry={() => void currencies.refetch()} /> : price.isError && !missing ? <ErrorState error={price.error} retry={() => void price.refetch()} /> : <div className="pricing-grid"><form className="pricing-card" onSubmit={submitBase}><div><strong>Reference price</strong><span>{missing ? 'Not priced' : formatMinorMoney(price.data!.adminVariantPrice.baseAmountMinor, price.data!.adminVariantPrice.baseCurrency)}</span></div><label className="field"><span>Amount *</span><input inputMode="decimal" onChange={(event) => setBaseAmount(event.target.value)} placeholder="100.00" value={baseAmount} /></label><label className="field"><span>Currency *</span><select onChange={(event) => setBaseCurrency(event.target.value)} value={baseCurrency}>{enabledCurrencies.map((currency) => <option key={currency.code}>{currency.code}</option>)}</select></label><Button disabled={setBase.isPending || !selectedVariant} type="submit">{setBase.isPending ? 'Saving…' : missing ? 'Create price' : 'Update price'}</Button></form><div className="pricing-card"><div><strong>Currency overrides</strong><span>{price.data?.adminVariantPrice.overrides.length ?? 0} configured</span></div>{price.data?.adminVariantPrice.overrides.length ? <ul className="override-list">{price.data.adminVariantPrice.overrides.map((item) => <li key={item.currency}><span><strong>{formatMinorMoney(item.amountMinor, item.currency)}</strong><small>{item.priceList}</small></span><Button aria-label={`Remove ${item.currency} override`} disabled={removeOverride.isPending} onClick={() => removeOverride.mutate(item.currency)} size="compact" variant="ghost">Remove</Button></li>)}</ul> : <p className="muted">No overrides.</p>}<form className="inline-price-form" onSubmit={submitOverride}><label className="field"><span>Currency</span><select onChange={(event) => setOverrideCurrency(event.target.value)} value={overrideCurrency}>{enabledCurrencies.map((currency) => <option key={currency.code}>{currency.code}</option>)}</select></label><label className="field"><span>Amount</span><input inputMode="decimal" onChange={(event) => setOverrideAmount(event.target.value)} placeholder="100.00" value={overrideAmount} /></label><Button disabled={setOverride.isPending || missing} type="submit">{setOverride.isPending ? 'Saving…' : 'Set override'}</Button></form>{missing && <span className="field-meta">Create the reference price first.</span>}</div></div>}</>}</section>
  );
}
