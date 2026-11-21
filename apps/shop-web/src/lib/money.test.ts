import { describe, expect, it } from 'vitest';
import { formatMinor, formatMoney } from './money';

describe('shop money formatting', () => {
  it('keeps the server two-decimal string exact', () => {
    expect(formatMoney({ amount: '12345678901234567890.42', currency: 'USD' }))
      .toBe('USD 12,345,678,901,234,567,890.42');
  });

  it('formats negative values without floating-point conversion', () => {
    expect(formatMoney({ amount: '-2500.05', currency: 'SGD' })).toBe('SGD -2,500.05');
  });

  it('formats opaque minor-unit strings with bigint', () => {
    expect(formatMinor('900719925474099301', 'USD')).toBe('USD 9,007,199,254,740,993.01');
  });

  it('returns a safe placeholder for malformed minor units', () => {
    expect(formatMinor('not-money', 'USD')).toBe('USD —');
  });

  it('does not turn malformed display money into a real-looking zero price', () => {
    expect(formatMoney({ amount: 'not-money', currency: 'USD' })).toBe('USD —');
  });
});
