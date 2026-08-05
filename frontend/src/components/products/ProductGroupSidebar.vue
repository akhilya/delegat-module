<script setup>
import { ref, nextTick, watch, onMounted, onBeforeUnmount } from "vue";
import { Plus, Copy } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import { t } from "@/i18n";
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuTrigger,
} from "@/components/ui/context-menu";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";

const props = defineProps({
  group: { type: Object, default: null },
  loading: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
  addUrl: { type: String, default: null },
  tab: { type: String, default: null },
  getDuplicateUrl: { type: Function, default: null },
  /** Parent row width below this (px) → photos-only rail. */
  compactBelow: { type: Number, default: 900 },
});

const rootRef = ref(null);
const scrollRef = ref(null);
const compact = ref(false);

let detachWheel = null;
let resizeObserver = null;

function onWheel(e) {
  const el = scrollRef.value;
  if (!el || el.scrollHeight <= el.clientHeight) return;
  const atTop = el.scrollTop <= 0;
  const atBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - 1;
  if ((e.deltaY < 0 && atTop) || (e.deltaY > 0 && atBottom)) return;
  e.preventDefault();
  el.scrollTop += e.deltaY;
}

function bindWheel() {
  detachWheel?.();
  detachWheel = null;
  const el = scrollRef.value;
  if (!el) return;
  el.addEventListener("wheel", onWheel, { passive: false });
  detachWheel = () => el.removeEventListener("wheel", onWheel);
}

function updateCompact(width) {
  compact.value = width < props.compactBelow;
}

function bindResize() {
  resizeObserver?.disconnect();
  resizeObserver = null;
  const parent = rootRef.value?.parentElement;
  if (!parent || typeof ResizeObserver === "undefined") return;
  resizeObserver = new ResizeObserver((entries) => {
    const w = entries[0]?.contentRect?.width;
    if (typeof w === "number") updateCompact(w);
  });
  resizeObserver.observe(parent);
  updateCompact(parent.getBoundingClientRect().width);
}

watch(
  () => [props.loading, props.group, props.disabled, compact.value],
  () => nextTick(bindWheel),
  { flush: "post", immediate: true },
);

onMounted(() => nextTick(bindResize));

onBeforeUnmount(() => {
  detachWheel?.();
  detachWheel = null;
  resizeObserver?.disconnect();
  resizeObserver = null;
});

function scrollCurrentIntoView() {
  nextTick(() => {
    const container = scrollRef.value;
    if (!container) return;
    const currentEl = container.querySelector("[data-current-member]");
    if (!currentEl) return;
    const containerRect = container.getBoundingClientRect();
    const elRect = currentEl.getBoundingClientRect();
    const scrollAdj =
      elRect.top -
      containerRect.top -
      (container.clientHeight - currentEl.offsetHeight) / 2;
    container.scrollTop += scrollAdj;
  });
}

function getMemberRoute(member) {
  const route = { path: `/products/${member.productId}/edit` };
  if (props.tab) route.query = { tab: props.tab };
  return route;
}

function memberLabel(member) {
  const name = member.productName || member.productSku || "";
  if (member.productSku && member.productName) {
    return `${member.productName} · ${member.productSku}`;
  }
  return name;
}

defineExpose({ scrollCurrentIntoView, scrollRef, compact });
</script>

<template>
  <!-- Always reserve column width on lg+ to avoid layout flicker while group loads. -->
  <div
    ref="rootRef"
    class="product-group-sidebar hidden lg:flex shrink-0 flex-col self-start overflow-hidden rounded-xl border bg-card text-card-foreground sticky top-14 h-[calc(100svh-3.5rem-1rem-4.75rem)]"
    :class="compact ? 'product-group-sidebar--compact' : 'product-group-sidebar--full'"
    :data-compact="compact ? '' : undefined"
  >
    <div
      v-if="loading"
      class="flex min-h-0 flex-1 items-center justify-center py-6"
    >
      <Spinner size="lg" class="text-muted-foreground" />
    </div>

    <TooltipProvider v-else :delay-duration="300">
      <div
        ref="scrollRef"
        class="thin-scrollbar min-h-0 flex-1 overflow-y-auto overscroll-contain"
        :class="[
          compact ? 'space-y-1.5 p-2' : 'space-y-2 p-3',
          { 'pointer-events-none opacity-50': disabled },
        ]"
      >
        <Tooltip v-if="addUrl">
          <TooltipTrigger as-child>
            <router-link
              :to="addUrl"
              class="flex items-center justify-center rounded-lg border border-dashed transition-colors hover:bg-accent/50"
              :class="compact ? 'size-12 mx-auto' : 'h-16 p-2'"
              :aria-label="t('products.productGroupSidebar.addProduct')"
            >
              <Plus class="size-4 text-muted-foreground" />
            </router-link>
          </TooltipTrigger>
          <TooltipContent side="right">
            {{ t("products.productGroupSidebar.addProduct") }}
          </TooltipContent>
        </Tooltip>
        <div
          v-else-if="group"
          class="flex items-center justify-center rounded-lg bg-accent"
          :class="compact ? 'size-12 mx-auto' : 'h-16 p-2'"
          aria-hidden="true"
        >
          <Plus class="size-4 text-muted-foreground" />
        </div>

        <template v-if="group">
          <ContextMenu v-for="member in group.members" :key="member.productId">
            <Tooltip :disabled="!compact">
              <TooltipTrigger as-child>
                <ContextMenuTrigger as-child>
                  <router-link
                    :to="getMemberRoute(member)"
                    class="transition-colors"
                    :class="
                      compact
                        ? [
                            'mx-auto flex size-12 items-center justify-center overflow-hidden rounded-lg',
                            member.isCurrent
                              ? 'ring-2 ring-primary ring-offset-2 ring-offset-card'
                              : 'hover:ring-2 hover:ring-border',
                          ]
                        : [
                            'flex items-center gap-3 rounded-lg p-2',
                            member.isCurrent
                              ? 'bg-primary font-medium text-primary-foreground hover:bg-primary hover:text-primary-foreground'
                              : 'hover:bg-accent/50',
                          ]
                    "
                    v-bind="member.isCurrent ? { 'data-current-member': '' } : {}"
                    :aria-label="memberLabel(member)"
                    :aria-current="member.isCurrent ? 'page' : undefined"
                  >
                    <div
                      class="relative shrink-0 overflow-hidden rounded-md bg-muted"
                      :class="compact ? 'size-full rounded-lg' : 'size-14'"
                    >
                      <template v-if="member.imageThumbnailUrl || member.imageUrl">
                        <img
                          :src="member.imageThumbnailUrl ?? member.imageUrl"
                          alt=""
                          aria-hidden="true"
                          class="absolute inset-0 size-full scale-110 object-cover blur-md"
                        />
                        <img
                          :src="member.imageThumbnailUrl ?? member.imageUrl"
                          class="relative z-10 size-full object-contain"
                          :alt="compact ? '' : member.productName"
                        />
                      </template>
                    </div>
                    <div v-if="!compact" class="min-w-0">
                      <p class="truncate text-sm">
                        {{ member.productName || member.productSku }}
                      </p>
                      <p
                        class="truncate text-xs"
                        :class="
                          member.isCurrent
                            ? 'text-primary-foreground/70'
                            : 'text-muted-foreground'
                        "
                      >
                        {{ member.productSku }}
                      </p>
                    </div>
                  </router-link>
                </ContextMenuTrigger>
              </TooltipTrigger>
              <TooltipContent v-if="compact" side="right" class="max-w-xs">
                <p class="font-medium">{{ member.productName || member.productSku }}</p>
                <p v-if="member.productName && member.productSku" class="text-muted-foreground text-xs">
                  {{ member.productSku }}
                </p>
              </TooltipContent>
            </Tooltip>
            <ContextMenuContent v-if="getDuplicateUrl">
              <ContextMenuItem @click="$router.push(getDuplicateUrl(member.productId))">
                <Copy />
                {{ t("products.productGroupSidebar.duplicate") }}
              </ContextMenuItem>
            </ContextMenuContent>
          </ContextMenu>
        </template>
      </div>
    </TooltipProvider>
  </div>
</template>

<style scoped>
.product-group-sidebar--full {
  width: clamp(14rem, 22vw, 24rem);
}

.product-group-sidebar--compact {
  width: 4.25rem;
}

@media (prefers-reduced-motion: no-preference) {
  .product-group-sidebar--full,
  .product-group-sidebar--compact {
    transition: width 0.2s ease;
  }
}
</style>
