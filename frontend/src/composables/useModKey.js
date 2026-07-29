import { computed } from "vue";

/** True when the user agent looks like macOS / iOS. */
export function isApplePlatform() {
  if (typeof navigator === "undefined") return false;
  return /Mac|iPhone|iPad|iPod/i.test(navigator.platform || navigator.userAgent || "");
}

/** Modifier key label for shortcuts: ⌘ on Apple, Ctrl elsewhere. */
export function modKeyLabel() {
  return isApplePlatform() ? "⌘" : "Ctrl";
}

/** Reactive mod key label for templates. */
export function useModKey() {
  return computed(() => modKeyLabel());
}
