import { describe, expect, it } from 'vitest';
import { formatMinorMoney, isMoneyText, minorToAmount } from './money';

describe('money boundary helpers', () => {
  it('formats arbitrarily large minor-unit strings without Number conversion', () => {
    expect(minorToAmount('900719925474099312')).toBe('9007199254740993.12');
    expect(formatMinorMoney('900719925474099312', 'SGD')).toBe('SGD 9,007,199,254,740,993.12');
  });

  it('accepts GraphQL Long values decoded as JSON numbers', () => {
    expect(minorToAmount(12345)).toBe('123.45');
    expect(formatMinorMoney(12345, 'SGD')).toBe('SGD 123.45');
  });

  it('accepts only the backend two-decimal wire format', () => {
    expect(isMoneyText('12.00')).toBe(true);
    expect(isMoneyText('12')).toBe(false);
    expect(isMoneyText('12.001')).toBe(false);
  });
});
