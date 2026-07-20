import { getLocale, toBcp47 } from "@/i18n";

export const ADDABLE_CURRENCIES = [
  { code: "USD", label: "USD — US Dollar" },
  { code: "EUR", label: "EUR — Euro" },
  { code: "KZT", label: "KZT — Tenge" },
  { code: "BYN", label: "BYN — Belarusian ruble" },
  { code: "CNY", label: "CNY — Yuan" },
  { code: "GBP", label: "GBP — Pound" },
  { code: "TRY", label: "TRY — Lira" },
];

export function getBaseCurrency(store) {
  if (!store) return null;
  if (store.baseCurrency) return store.baseCurrency.toUpperCase();
  const base = store.currencies?.find((c) => c.isBase);
  return base?.code?.toUpperCase() ?? null;
}

export function getBaseCurrencyFromList(currencies = []) {
  const base = currencies.find((c) => c.isBase);
  return base?.code?.toUpperCase() ?? null;
}

export function convertToBase(amount, currencyCode, currencies = [], baseCurrency) {
  const base = (baseCurrency || getBaseCurrencyFromList(currencies))?.toUpperCase();
  if (!base) return amount;
  const code = (currencyCode || base).toUpperCase();
  if (code === base) return amount;
  const row = currencies.find((c) => c.code === code && c.isEnabled !== false);
  if (!row?.rateToBase) return null;
  return Math.round(amount * row.rateToBase * 100) / 100;
}

export function convertFromBase(baseAmount, currencyCode, currencies = [], baseCurrency) {
  const base = (baseCurrency || getBaseCurrencyFromList(currencies))?.toUpperCase();
  if (!base) return baseAmount;
  const code = (currencyCode || base).toUpperCase();
  if (code === base) return baseAmount;
  const row = currencies.find((c) => c.code === code && c.isEnabled !== false);
  if (!row?.rateToBase) return null;
  return Math.round((baseAmount / row.rateToBase) * 100) / 100;
}

export function formatMoney(amount, currency) {
  if (amount == null || Number.isNaN(amount)) return "—";
  const code = currency?.toUpperCase() || "";
  return `${Number(amount).toLocaleString(toBcp47(getLocale()), { minimumFractionDigits: 0, maximumFractionDigits: 2 })} ${code}`.trim();
}

export function formatEquivalents(baseAmount, currencies = [], baseCurrency) {
  if (baseAmount == null) return [];
  const base = baseCurrency || getBaseCurrencyFromList(currencies);
  return currencies
    .filter((c) => !c.isBase && c.isEnabled !== false)
    .map((c) => {
      const v = convertFromBase(baseAmount, c.code, currencies, base);
      return v != null ? `≈ ${formatMoney(v, c.code)}` : null;
    })
    .filter(Boolean);
}