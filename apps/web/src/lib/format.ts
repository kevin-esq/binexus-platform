export function formatMoney(totalCents: number, currency: string): string {
  const amount = totalCents / 100;
  try {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(amount);
  } catch {
    return `${amount.toFixed(2)} ${currency}`;
  }
}

export function shortId(id: string): string {
  return id.length > 10 ? id.slice(-8) : id;
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleString();
}
