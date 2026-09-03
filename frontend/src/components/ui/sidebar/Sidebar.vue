<script setup>
import { cn } from "@/lib/utils";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";
import SheetResizeHandle from "@/components/ui/sheet/SheetResizeHandle.vue";
import {
  SIDEBAR_INSET_GUTTER,
  SIDEBAR_WIDTH_MOBILE,
  useSidebar,
} from "./utils";

defineOptions({
  inheritAttrs: false,
});

const props = defineProps({
  side: { type: String, default: "left" },
  variant: { type: String, default: "sidebar" },
  collapsible: { type: String, default: "offcanvas" },
  class: { type: null, required: false },
});

const { isMobile, state, openMobile, setOpenMobile, isResizing, startResize } =
  useSidebar();
</script>

<template>
  <div
    v-if="collapsible === 'none'"
    data-slot="sidebar"
    :class="
      cn(
        'flex h-full w-(--sidebar-width) flex-col bg-sidebar text-sidebar-foreground',
        props.class,
      )
    "
    v-bind="$attrs"
  >
    <slot />
  </div>

  <Sheet
    v-else-if="isMobile"
    :open="openMobile"
    v-bind="$attrs"
    @update:open="setOpenMobile"
  >
    <SheetContent
      data-sidebar="sidebar"
      data-mobile="true"
      inset
      :show-close="false"
      :side="side"
      class="bg-sidebar p-0 text-sidebar-foreground"
      :style="{
        '--sidebar-width': SIDEBAR_WIDTH_MOBILE,
      }"
    >
      <SheetHeader class="sr-only">
        <SheetTitle>Sidebar</SheetTitle>
        <SheetDescription>Displays the mobile sidebar.</SheetDescription>
      </SheetHeader>
      <div class="flex h-full w-full flex-col">
        <slot />
      </div>
    </SheetContent>
  </Sheet>

  <div
    v-else
    class="group peer hidden text-sidebar-foreground md:block"
    data-slot="sidebar"
    :data-state="state"
    :data-collapsible="state === 'collapsed' ? collapsible : ''"
    :data-variant="variant"
    :data-side="side"
    :data-resizing="isResizing ? '' : undefined"
  >
    <div
      :class="
        cn(
          'relative bg-transparent',
          isResizing
            ? 'will-change-[width]'
            : 'transition-[width] duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]',
          'w-[calc(var(--sidebar-width)+var(--sidebar-inset-gutter,1rem))]',
          'group-data-[collapsible=offcanvas]:w-0',
          'group-data-[side=right]:rotate-180',
          'group-data-[collapsible=icon]:w-[calc(var(--sidebar-width-icon)+var(--sidebar-inset-gutter,1rem))]',
        )
      "
      :style="{ '--sidebar-inset-gutter': SIDEBAR_INSET_GUTTER }"
    />
    <div
      data-slot="sidebar-panel"
      data-inset
      :class="
        cn(
          'fixed z-10 hidden rounded-xl md:flex',
          'top-2 bottom-2 h-auto w-(--sidebar-width)',
          isResizing
            ? 'will-change-[width]'
            : 'transition-[left,right,width] duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]',
          side === 'left'
            ? 'left-2 group-data-[collapsible=offcanvas]:left-[calc((var(--sidebar-width)+0.5rem)*-1)]'
            : 'right-2 group-data-[collapsible=offcanvas]:right-[calc((var(--sidebar-width)+0.5rem)*-1)]',
          'group-data-[collapsible=icon]:w-(--sidebar-width-icon)',
          props.class,
        )
      "
      v-bind="$attrs"
    >
      <div
        data-sidebar="sidebar"
        class="relative flex h-full w-full min-w-0 flex-col overflow-hidden rounded-xl bg-sidebar"
      >
        <slot />
      </div>
      <SheetResizeHandle
        v-if="state === 'expanded'"
        flush
        :side="side"
        :resizing="isResizing"
        @pointerdown="startResize"
      />
    </div>
  </div>
</template>
