import { createContext } from "reka-ui";

export const SIDEBAR_COOKIE_NAME = "sidebar_state";
export const SIDEBAR_COOKIE_MAX_AGE = 60 * 60 * 24 * 7;
export const SIDEBAR_WIDTH = "16rem";
export const SIDEBAR_WIDTH_MOBILE = "18rem";
export const SIDEBAR_WIDTH_ICON = "3rem";
export const SIDEBAR_KEYBOARD_SHORTCUT = "b";
/** 8px float on each side of the desktop panel (matches sheet inset). */
export const SIDEBAR_INSET_GUTTER = "1rem";

export const [useSidebar, provideSidebarContext] = createContext("Sidebar");