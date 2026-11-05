import { useDark, useToggle } from "@vueuse/core";

/**
 * App color scheme. Bootstrapped from main.js so Auth/Setup and the app shell
 * share one theme. Previously useDark ran only inside UserAccountMenu, so Setup
 * stayed on :root (light) and flipped to system dark when the sidebar mounted.
 *
 * index.html applies the same storage key before first paint to avoid a white flash.
 * initialValue: light matches DESIGN.md; toggle still persists in localStorage.
 */
export const isDark = useDark({
  initialValue: "light",
});

export const toggleDark = useToggle(isDark);
