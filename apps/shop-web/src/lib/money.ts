export interface DisplayMoney { amount: string; currency: string }

export function formatMoney(money: DisplayMoney): string {
  if (!/^-?\d+\.\d{2}$/.test(money.amount)) return `${money.currency} —`;
  const normalized = money.amount;
  const [whole = '0', fraction = '00'] = normalized.split('.');
  const negative = whole.startsWith('-');
  const digits = negative ? whole.slice(1) : whole;
  const grouped = digits.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return `${money.currency} ${negative ? '-' : ''}${grouped}.${fraction}`;
}

export function formatMinor(minor: string, currency: string): string {
  try {
    const value = BigInt(minor);
    const negative = value < 0n;
    const absolute = negative ? -value : value;
    const whole = (absolute / 100n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    const fraction = (absolute % 100n).toString().padStart(2, '0');
    return `${currency} ${negative ? '-' : ''}${whole}.${fraction}`;
  } catch {
    return `${currency} —`;
  }
}
