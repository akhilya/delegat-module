import { inject, provide, ref } from "vue";

export const SIDEBAR_CONTEXT_KEY = Symbol("sidebar");

export function provideSidebar(defaultOpen = false) {
  const open = ref(defaultOpen);

  function setOpen(value) {
    open.value = value;
  }

  function toggleSidebar() {
    open.value = !open.value;
  }

  const context = {
    open,
    setOpen,
    toggleSidebar,
  };

  provide(SIDEBAR_CONTEXT_KEY, context);

  return context;
}

export function useSidebar() {
  const context = inject(SIDEBAR_CONTEXT_KEY, null);
  if (!context) {
    throw new Error("useSidebar must be used within SidebarProvider");
  }

  return context;
}
