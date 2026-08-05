<script setup>
import { computed, useSlots } from "vue";
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { cn } from "@/lib/utils";

const props = defineProps({
  /** Breadcrumb trail. Omit / empty + no #breadcrumbs slot → no breadcrumb row. */
  breadcrumbItems: {
    type: Array,
    default: null,
  },
  /**
   * Fill the viewport below the navbar (flex column, min-h-0).
   * Prefer this over putting h-screen on Page.
   */
  fillViewport: {
    type: Boolean,
    default: false,
  },
  stickyNav: {
    type: Boolean,
    default: true,
  },
  /**
   * Explicit override for navbar center column. Prefer this over slot detection when
   * the parent toggles `#nav-center` with v-if (Vue slot presence is unreliable then).
   */
  hasNavCenter: {
    type: Boolean,
    default: undefined,
  },
  class: { type: null, required: false },
  contentClass: { type: null, required: false },
});

const slots = useSlots();
const showNavCenter = computed(() => {
  if (props.hasNavCenter !== undefined) return props.hasNavCenter;
  return typeof slots["nav-center"] === "function";
});
</script>

<template>
  <Page
    :class="
      cn(
        fillViewport && 'h-svh max-h-svh min-h-0 overflow-hidden',
        props.class,
      )
    "
  >
    <NavigationBar class="shrink-0" :sticky="stickyNav" :has-center="showNavCenter">
      <template v-if="$slots['nav-left']" #left>
        <slot name="nav-left" />
      </template>
      <template v-if="showNavCenter" #center>
        <slot name="nav-center" />
      </template>
      <template v-if="$slots['nav-right']" #right>
        <slot name="nav-right" />
      </template>
    </NavigationBar>

    <!--
      Below-nav region: min height = 100svh − navbar so empty states can expand
      without Page growing past the viewport (nav + this = svh).
    -->
    <div
      :class="
        cn(
          'flex min-h-[calc(100svh-var(--app-navbar-height))] flex-1 flex-col',
          fillViewport && 'min-h-0 overflow-hidden',
        )
      "
    >
      <Breadcrumbs
        v-if="breadcrumbItems && breadcrumbItems.length"
        class="shrink-0"
        :items="breadcrumbItems"
      />
      <slot v-else name="breadcrumbs" />

      <div
        :class="
          cn(
            'flex min-h-0 flex-1 flex-col',
            !fillViewport && 'pb-4',
            props.contentClass,
          )
        "
      >
        <slot />
      </div>
    </div>
  </Page>
</template>
