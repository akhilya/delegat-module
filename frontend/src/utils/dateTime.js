import { getLocale, toBcp47 } from "@/i18n";

/** Technical fallback when store timezone is unknown (not a product catalog default). */
const FALLBACK_TZ = "UTC";

function intlLocale() {
  return toBcp47(getLocale());
}

export function getStoreTimezone(store) {
  return store?.timezone || FALLBACK_TZ;
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
export function formatDateTime(iso, timezone = FALLBACK_TZ, options = {}) {
  if (!iso) return { date: "-", time: "-", combined: "-" };
  const d = parseServerUtc(iso);
  if (!d) return { date: "-", time: "-", combined: "-" };
  const tz = timezone || FALLBACK_TZ;
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

export function formatDateTimeCell(iso, timezone = FALLBACK_TZ) {
  const { date, time } = formatDateTime(iso, timezone);
  return { date, time };
}

/** Single-line datetime; pass `{ withSeconds: true }` for logs. */
export function formatDateTimeCombined(iso, timezone = FALLBACK_TZ, options = {}) {
  const timeOpts = options.withSeconds
    ? { second: "2-digit", ...(options.time || {}) }
    : options.time;
  return formatDateTime(iso, timezone, { ...options, time: timeOpts }).combined;
}

/** Time only (HH:MM or with seconds). */
export function formatTimeOnly(iso, timezone = FALLBACK_TZ, options = {}) {
  return formatDateTime(iso, timezone, {
    time: { hour12: false, ...options },
  }).time;
}

/** Date only (pass Intl date options as the third argument). */
export function formatDateOnly(iso, timezone = FALLBACK_TZ, dateOptions = {}) {
  const d = parseServerUtc(iso);
  if (!d) return "-";
  const opts = {
    timeZone: timezone || FALLBACK_TZ,
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
export function calendarDayKey(iso, timezone = FALLBACK_TZ) {
  const d = parseServerUtc(iso) || (iso instanceof Date ? iso : null);
  if (!d || Number.isNaN(d.getTime())) return null;
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: timezone || FALLBACK_TZ,
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
export function calendarDayKeyNow(timezone = FALLBACK_TZ) {
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

/**
 * Human relative age from a server UTC timestamp.
 * Returns null when older than 7 days (caller should show absolute date).
 * @param {*} value
 * @param {number} [nowMs]
 * @param {(key: string, params?: object) => string} [translate]
 */
export function formatRelativeAgo(value, nowMs = Date.now(), translate) {
  const d = parseServerUtc(value);
  if (!d) return "-";
  const diffMs = Math.max(0, nowMs - d.getTime());
  const sec = Math.floor(diffMs / 1000);
  const tr = typeof translate === "function" ? translate : (k, p) => k;

  if (sec < 5) return tr("components.logs.justNow");
  if (sec < 60) return tr("components.logs.secondsAgo", { n: sec });
  const min = Math.floor(sec / 60);
  if (min < 60) return tr("components.logs.minutesAgo", { n: min });
  const hours = Math.floor(min / 60);
  if (hours < 24) return tr("components.logs.hoursAgo", { n: hours });
  const days = Math.floor(hours / 24);
  if (days < 7) return tr("components.logs.daysAgo", { n: days });
  return null;
}
