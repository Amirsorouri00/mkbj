'use client';

import { useEffect, useState } from 'react';

interface CurrencyPickerProps {
  currencies: ReadonlyArray<{ code: string }>;
}

export function CurrencyPicker({ currencies }: CurrencyPickerProps) {
  const [currency, setCurrency] = useState('USD');

  useEffect(() => {
    const current = new URL(window.location.href).searchParams.get('currency');
    if (current) setCurrency(current.toUpperCase());
  }, []);

  return (
    <label className="currency-picker">
      <span className="sr-only">Currency</span>
      <select
        aria-label="Currency"
        value={currency}
        onChange={(event) => {
          const next = event.target.value;
          setCurrency(next);
          const url = new URL(window.location.href);
          url.searchParams.set('currency', next);
          url.searchParams.delete('after');
          window.location.assign(url);
        }}
      >
        {currencies.map(({ code }) => <option key={code} value={code}>{code}</option>)}
      </select>
    </label>
  );
}
