import { DEFAULT_TIMEZONE } from "@/lib/timezones";
import { getLocale, toBcp47 } from "@/i18n";

function intlLocale() {
  return toBcp47(getLocale());
}

export function getStoreTimezone(store) {
  return store?.timezone || DEFAULT_TIMEZONE;
}

/**
 * Server timestamps are UTC (DateTime.UtcNow). Strings without a zone suffix
 * are treated as UTC so conversion to a store timezone is correct.
 */
export function parseServerUtc(value) {
  if (value == null || value === "") return null;
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value;
  const s = String(value).trim();
  if (!s) return null;
  const hasZone = /[zZ]$|[+-]\d{2}:?\d{2}$/.test(s);
  const d = new Date(hasZone ? s : `${s}Z`);
  return Number.isNaN(d.getTime()) ? null : d;
}

/**
 * Format a server timestamp in the given IANA timezone (store setting).
 * @returns {{ date: string, time: string, combined: string }}
 */
export function formatDateTime(iso, timezone = DEFAULT_TIMEZONE, options = {}) {
  if (!iso) return { date: "—", time: "—", combined: "—" };
  const d = parseServerUtc(iso);
  if (!d) return { date: "—", time: "—", combined: "—" };
  const tz = timezone || DEFAULT_TIMEZONE;
  const loc = intlLocale();
  const date = d.toLocaleDateString(loc, {
    timeZone: tz,
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    ...options.date,
  });
  const time = d.toLocaleTimeString(loc, {
    timeZone: tz,
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
    ...options.time,
  });
  return { date, time, combined: `${date} ${time}` };
}

export function formatDateTimeCell(iso, timezone = DEFAULT_TIMEZONE) {
  const { date, time } = formatDateTime(iso, timezone);
  return { date, time };
}

/** Single-line datetime; pass `{ withSeconds: true }` for logs. */
export function formatDateTimeCombined(iso, timezone = DEFAULT_TIMEZONE, options = {}) {
  const timeOpts = options.withSeconds
    ? { second: "2-digit", ...(options.time || {}) }
    : options.time;
  return formatDateTime(iso, timezone, { ...options, time: timeOpts }).combined;
}

/** Time only (HH:MM or with seconds). */
export function formatTimeOnly(iso, timezone = DEFAULT_TIMEZONE, options = {}) {
  return formatDateTime(iso, timezone, {
    time: { hour12: false, ...options },
  }).time;
}

/** Date only (pass Intl date options as the third argument). */
export function formatDateOnly(iso, timezone = DEFAULT_TIMEZONE, dateOptions = {}) {
  const d = parseServerUtc(iso);
  if (!d) return "—";
  const opts = {
    timeZone: timezone || DEFAULT_TIMEZONE,
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    ...dateOptions,
  };
  for (const key of Object.keys(opts)) {
    if (opts[key] === undefined) delete opts[key];
  }
  return d.toLocaleDateString(intlLocale(), opts);
}

/**
 * Calendar day key in a timezone: "YYYY-MM-DD" for bucketing (AI history, etc.).
 */
export function calendarDayKey(iso, timezone = DEFAULT_TIMEZONE) {
  const d = parseServerUtc(iso) || (iso instanceof Date ? iso : null);
  if (!d || Number.isNaN(d.getTime())) return null;
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: timezone || DEFAULT_TIMEZONE,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).formatToParts(d);
  const y = parts.find((p) => p.type === "year")?.value;
  const m = parts.find((p) => p.type === "month")?.value;
  const day = parts.find((p) => p.type === "day")?.value;
  if (!y || !m || !day) return null;
  return `${y}-${m}-${day}`;
}

/** Today / yesterday / N days ago keys relative to `now` in the store timezone. */
export function calendarDayKeyNow(timezone = DEFAULT_TIMEZONE) {
  return calendarDayKey(new Date(), timezone);
}

export function addCalendarDays(dayKey, deltaDays) {
  if (!dayKey) return null;
  const [y, m, d] = dayKey.split("-").map(Number);
  const utc = new Date(Date.UTC(y, m - 1, d + deltaDays));
  const yy = utc.getUTCFullYear();
  const mm = String(utc.getUTCMonth() + 1).padStart(2, "0");
  const dd = String(utc.getUTCDate()).padStart(2, "0");
  return `${yy}-${mm}-${dd}`;
}
