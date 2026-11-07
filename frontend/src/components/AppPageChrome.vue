<script setup>
import { computed, onMounted, onUnmounted, ref, useSlots } from "vue";
import { useRoute } from "vue-router";
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { cn } from "@/lib/utils";
import { t, i18n } from "@/i18n";
import { resolvePageBreadcrumbs } from "@/lib/pageBreadcrumbs";
import { breadcrumbParentLabel, pageTitleDetail } from "@/lib/pageChromeState";
import { getCachedMe, ME_UPDATED_EVENT } from "@/lib/meCache";
import { canAccessPath } from "@/lib/routeAccess";
import { PAGE_SETTINGS } from "@/router";

const props = defineProps({
  /**
   * Full trail override. Omit to derive crumbs from the route + `setPageTitle`.
   */
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
const route = useRoute();
const me = ref(getCachedMe() ?? null);
const navbarHeight = ref(64);

const showNavCenter = computed(() => {
  if (props.hasNavCenter !== undefined) return props.hasNavCenter;
  return typeof slots["nav-center"] === "function";
});

const canPlatformSettings = computed(() =>
  canAccessPath(me.value, PAGE_SETTINGS),
);

const resolvedBreadcrumbs = computed(() => {
  if (props.breadcrumbItems?.length) return props.breadcrumbItems;
  const locale = i18n.global.locale.value;
  void locale;
  return resolvePageBreadcrumbs({
    name: route.name,
    path: route.path,
    query: route.query,
    params: route.params,
    meta: route.meta,
    detail: pageTitleDetail.value,
    parentLabel: breadcrumbParentLabel.value,
    t,
    canPlatformSettings: canPlatformSettings.value,
  });
});

function onMeUpdated(event) {
  if (event?.detail && typeof event.detail === "object") {
    me.value = event.detail;
  } else {
    me.value = getCachedMe() ?? null;
  }
}

onMounted(() => {
  window.addEventListener(ME_UPDATED_EVENT, onMeUpdated);
});

onUnmounted(() => {
  window.removeEventListener(ME_UPDATED_EVENT, onMeUpdated);
});
</script>

<template>
  <Page
    data-slot="app-page"
    :style="{ '--app-navbar-height': `${navbarHeight}px` }"
    :class="
      cn(
        fillViewport &&
          '!min-h-0 h-full max-h-full min-w-0 w-full overflow-hidden',
        showNavCenter && 'has-nav-tools',
        props.class,
      )
    "
  >
    <Teleport defer to="#app-navbar-host">
      <NavigationBar
        class="w-full min-w-0 bg-background adaptive-padding"
        :has-center="showNavCenter"
        @resize="navbarHeight = $event"
      >
        <template #left>
          <slot v-if="$slots['nav-left']" name="nav-left" />
          <Breadcrumbs
            v-else-if="resolvedBreadcrumbs.length"
            inset
            :items="resolvedBreadcrumbs"
          />
        </template>
        <template v-if="$slots['nav-center']" #center>
          <slot name="nav-center" />
        </template>
        <template v-if="$slots['nav-right']" #right>
          <slot name="nav-right" />
        </template>
        <template v-if="$slots['nav-tabs']" #tabs>
          <slot name="nav-tabs" />
        </template>
      </NavigationBar>
    </Teleport>

    <!--
      Below-nav region. min-h-full on Page fills the inset scroller so empty
      states can expand. fillViewport clips to the pane for nested canvases.
    -->
    <div
      :class="
        cn(
          'flex min-h-0 min-w-0 flex-1 flex-col',
          fillViewport && 'overflow-hidden',
        )
      "
    >
      <div
        :class="
          cn(
            'flex min-h-0 min-w-0 flex-1 flex-col pb-[var(--page-scroll-end)]',
            props.contentClass,
          )
        "
      >
        <slot />
      </div>
    </div>
  </Page>
</template>
