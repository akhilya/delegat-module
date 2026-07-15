import { DEFAULT_TIMEZONE } from "@/lib/timezones";

export function getStoreTimezone(store) {
  return store?.timezone || DEFAULT_TIMEZONE;
}

export function formatDateTime(iso, timezone = DEFAULT_TIMEZONE, options = {}) {
  if (!iso) return "—";
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return "—";
  const date = d.toLocaleDateString("ru-RU", {
    timeZone: timezone,
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    ...options.date,
  });
  const time = d.toLocaleTimeString("ru-RU", {
    timeZone: timezone,
    hour: "2-digit",
    minute: "2-digit",
    ...options.time,
  });
  return { date, time, combined: `${date} ${time}` };
}

export function formatDateTimeCell(iso, timezone = DEFAULT_TIMEZONE) {
  const { date, time } = formatDateTime(iso, timezone);
  return { date, time };
}