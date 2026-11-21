export function isMoneyText(value: string): boolean {
  return /^\d+\.\d{2}$/.test(value);
}

type MinorUnits = string | number | bigint;

export function minorToAmount(value: MinorUnits): string {
  const text = String(value);
  const negative = text.startsWith('-');
  const digits = (negative ? text.slice(1) : text).padStart(3, '0');
  return `${negative ? '-' : ''}${digits.slice(0, -2)}.${digits.slice(-2)}`;
}

export function formatMinorMoney(value: MinorUnits, currency: string): string {
  const amount = minorToAmount(value);
  const [whole, fraction] = amount.split('.');
  const grouped = whole!.replace(/\B(?=(\d{3})+(?!\d))/g, ',');
  return `${currency} ${grouped}.${fraction}`;
}
