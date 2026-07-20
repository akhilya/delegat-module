import { t } from "@/i18n";

/** Common IANA timezones for store settings. */
export const STORE_TIMEZONES = [
  { value: "Europe/Moscow", label: t("lib.timezones.moscow") },
  { value: "Europe/Kaliningrad", label: t("lib.timezones.kaliningrad") },
  { value: "Europe/Samara", label: t("lib.timezones.samara") },
  { value: "Asia/Yekaterinburg", label: t("lib.timezones.yekaterinburg") },
  { value: "Asia/Omsk", label: t("lib.timezones.omsk") },
  { value: "Asia/Krasnoyarsk", label: t("lib.timezones.krasnoyarsk") },
  { value: "Asia/Irkutsk", label: t("lib.timezones.irkutsk") },
  { value: "Asia/Yakutsk", label: t("lib.timezones.yakutsk") },
  { value: "Asia/Vladivostok", label: t("lib.timezones.vladivostok") },
  { value: "Asia/Magadan", label: t("lib.timezones.magadan") },
  { value: "Asia/Kamchatka", label: t("lib.timezones.kamchatka") },
  { value: "Asia/Almaty", label: t("lib.timezones.almaty") },
  { value: "Asia/Tashkent", label: t("lib.timezones.tashkent") },
  { value: "Europe/Minsk", label: t("lib.timezones.minsk") },
  { value: "Europe/Kyiv", label: t("lib.timezones.kyiv") },
  { value: "UTC", label: "UTC" },
];

export const DEFAULT_TIMEZONE = "Europe/Moscow";
