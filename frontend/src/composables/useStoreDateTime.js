import { computed } from "vue";
import { t } from "@/i18n";
import { useSelectedStore } from "@/composables/useSelectedStore";
import {
  addCalendarDays,
  calendarDayKey,
  calendarDayKeyNow,
  formatDateOnly,
  formatDateTimeCombined,
  formatTimeOnly,
} from "@/utils/dateTime";

/**
 * Date/time formatting bound to the selected store's timezone from the DB.
 */
export function useStoreDateTime() {
  const { storeTimezone, loadMemberships, selectedStore } = useSelectedStore();

  const timezone = computed(
    () => storeTimezone.value || selectedStore.value?.timezone || "UTC",
  );

  function formatDateTime(iso, options = {}) {
    return formatDateTimeCombined(iso, timezone.value, options);
  }

  function formatTime(iso, options = {}) {
    return formatTimeOnly(iso, timezone.value, options);
  }

  function formatDate(iso, options = {}) {
    return formatDateOnly(iso, timezone.value, options);
  }

  function dayKey(iso) {
    return calendarDayKey(iso ?? new Date(), timezone.value);
  }

  function todayKey() {
    return calendarDayKeyNow(timezone.value);
  }

  function relativeDayLabel(iso) {
    const key = dayKey(iso);
    if (!key) return "-";
    const today = todayKey();
    const yesterday = addCalendarDays(today, -1);
    if (key === today) return t("composables.useStoreDateTime.today");
    if (key === yesterday) return t("composables.useStoreDateTime.yesterday");
    const [y, m, d] = key.split("-").map(Number);
    // Format short date in store TZ using a noon UTC stand-in for that calendar day
    const standIn = new Date(Date.UTC(y, m - 1, d, 12, 0, 0));
    // Strip the trailing "g." (Russian year abbreviation, \u0433) Intl may append in ru-RU formatting
    return formatDateOnly(standIn.toISOString(), timezone.value, {
      day: "numeric",
      month: "short",
      year: undefined,
    }).replace(/\s*\u0433\.?\s*$/i, "").trim();
  }

  return {
    timezone,
    storeTimezone: timezone,
    loadMemberships,
    formatDateTime,
    formatTime,
    formatDate,
    dayKey,
    todayKey,
    relativeDayLabel,
    addCalendarDays,
  };
}
