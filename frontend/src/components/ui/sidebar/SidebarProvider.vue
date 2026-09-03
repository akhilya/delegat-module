<script setup>
import { computed, ref } from "vue";
import {
  defaultDocument,
  useEventListener,
  useMediaQuery,
  useVModel,
} from "@vueuse/core";
import { TooltipProvider } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";
import {
  SHEET_WIDTH_DEFAULTS,
  SHEET_WIDTH_STORAGE,
  SIDEBAR_RESIZE_MAX,
  SIDEBAR_RESIZE_MIN,
  useSheetResize,
} from "@/composables/useSheetResize";
import {
  provideSidebarContext,
  SIDEBAR_COOKIE_MAX_AGE,
  SIDEBAR_COOKIE_NAME,
  SIDEBAR_KEYBOARD_SHORTCUT,
  SIDEBAR_WIDTH_ICON,
} from "./utils";

const props = defineProps({
  defaultOpen: {
    type: Boolean,
    default: !defaultDocument?.cookie.includes(`${SIDEBAR_COOKIE_NAME}=false`),
  },
  open: { type: Boolean, default: undefined },
  class: { type: null, required: false },
});

const emits = defineEmits(["update:open"]);

const isMobile = useMediaQuery("(max-width: 768px)");
const openMobile = ref(false);

const open = useVModel(props, "open", emits, {
  defaultValue: props.defaultOpen ?? true,
  passive: props.open === undefined,
});

function setOpen(value) {
  open.value = value;
  document.cookie = `${SIDEBAR_COOKIE_NAME}=${open.value}; path=/; max-age=${SIDEBAR_COOKIE_MAX_AGE}`;
}

function setOpenMobile(value) {
  openMobile.value = value;
}

function toggleSidebar() {
  return isMobile.value
    ? setOpenMobile(!openMobile.value)
    : setOpen(!open.value);
}

useEventListener("keydown", (event) => {
  if (
    event.key === SIDEBAR_KEYBOARD_SHORTCUT &&
    (event.metaKey || event.ctrlKey)
  ) {
    event.preventDefault();
    toggleSidebar();
  }
});

const state = computed(() => (open.value ? "expanded" : "collapsed"));

const { width, isResizing, startResize } = useSheetResize({
  storageKey: SHEET_WIDTH_STORAGE.sidebar,
  defaultWidth: SHEET_WIDTH_DEFAULTS.sidebar,
  minWidth: SIDEBAR_RESIZE_MIN,
  maxWidth: SIDEBAR_RESIZE_MAX,
  side: "left",
});

provideSidebarContext({
  state,
  open,
  setOpen,
  isMobile,
  openMobile,
  setOpenMobile,
  toggleSidebar,
  width,
  isResizing,
  startResize,
});
</script>

<template>
  <TooltipProvider :delay-duration="0">
    <div
      data-slot="sidebar-wrapper"
      :style="{
        '--sidebar-width': `${width}px`,
        '--sidebar-width-icon': SIDEBAR_WIDTH_ICON,
      }"
      :class="
        cn(
          'group/sidebar-wrapper bg-background flex min-h-svh w-full',
          props.class,
        )
      "
      v-bind="$attrs"
    >
      <slot />
    </div>
  </TooltipProvider>
</template>
