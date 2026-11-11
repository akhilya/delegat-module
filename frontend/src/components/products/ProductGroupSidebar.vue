<script setup>
import { ref, nextTick, watch, onMounted, onBeforeUnmount } from "vue";
import { Plus, Copy, Image } from "lucide-vue-next";
import { Spinner } from "@/components/ui/spinner";
import AppImage from "@/components/AppImage.vue";
import ProductImageCell from "@/components/products/ProductImageCell.vue";
import { t } from "@/i18n";
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuGroup,
  ContextMenuItem,
  ContextMenuLabel,
  ContextMenuSeparator,
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

function memberThumbSrc(member) {
  return member.imageThumbnailUrl || member.imageUrl || null;
}

function memberPreviewSrc(member) {
  return member.imageUrl || member.imageThumbnailUrl || null;
}

defineExpose({ scrollCurrentIntoView, scrollRef, compact });
</script>

<template>
  <!-- Always reserve column width on lg+ to avoid layout flicker while group loads. -->
  <div
    ref="rootRef"
    class="product-group-sidebar hidden lg:flex shrink-0 flex-col self-start overflow-hidden rounded-xl border bg-card text-card-foreground sticky top-0 h-[var(--app-fill-pane-height)]"
    :class="
      compact ? 'product-group-sidebar--compact' : 'product-group-sidebar--full'
    "
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
            <!--
              ContextMenuTrigger must wrap Tooltip, not the reverse.
              Its MenuAnchor is a 0×0 virtual point at (0, 0) until a
              right-click; nested inside Tooltip it steals the popper
              and the preview opens at the top-left of the viewport.
            -->
            <ContextMenuTrigger as-child>
              <div :class="compact ? 'mx-auto size-12' : 'min-w-0 w-full'">
                <Tooltip :disabled="!compact">
                  <TooltipTrigger as-child>
                    <router-link
                      :to="getMemberRoute(member)"
                      class="transition-colors"
                      :class="
                        compact
                          ? [
                              'flex size-full items-center justify-center overflow-hidden rounded-lg',
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
                      v-bind="
                        member.isCurrent ? { 'data-current-member': '' } : {}
                      "
                      :aria-label="memberLabel(member)"
                      :aria-current="member.isCurrent ? 'page' : undefined"
                    >
                      <ProductImageCell
                        v-if="!compact"
                        :thumbnail-url="member.imageThumbnailUrl || null"
                        :image-url="member.imageUrl || null"
                        :stop-events="false"
                      >
                        <AppImage
                          :src="memberThumbSrc(member)"
                          :alt="member.productName"
                          blur-backdrop
                          spinner-size="sm"
                          :class="[
                            'size-14 shrink-0 rounded-md',
                            !memberThumbSrc(member)
                              ? 'bg-muted/40 flex items-center justify-center'
                              : 'bg-muted',
                          ]"
                          img-class="relative z-10 size-full object-contain"
                        >
                          <Image
                            class="text-muted-foreground size-[25%]"
                            aria-hidden="true"
                          />
                        </AppImage>
                      </ProductImageCell>
                      <AppImage
                        v-else
                        :src="memberThumbSrc(member)"
                        alt=""
                        blur-backdrop
                        spinner-size="xs"
                        :class="[
                          'size-full shrink-0 rounded-lg',
                          !memberThumbSrc(member)
                            ? 'bg-muted/40 flex items-center justify-center'
                            : 'bg-muted',
                        ]"
                        img-class="relative z-10 size-full object-contain"
                      >
                        <Image
                          class="text-muted-foreground size-[25%]"
                          aria-hidden="true"
                        />
                      </AppImage>
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
                  </TooltipTrigger>
                  <TooltipContent
                    v-if="compact"
                    side="right"
                    :side-offset="8"
                    :class="
                      memberPreviewSrc(member)
                        ? 'max-w-none overflow-hidden p-1.5'
                        : 'max-w-xs'
                    "
                  >
                    <div class="flex flex-col gap-1.5">
                      <AppImage
                        v-if="memberPreviewSrc(member)"
                        :src="memberPreviewSrc(member)"
                        alt=""
                        spinner-size="default"
                        class="flex size-64 items-center justify-center"
                        img-class="max-h-64 max-w-64 rounded-sm object-contain"
                      />
                      <div
                        :class="
                          memberPreviewSrc(member) ? 'px-0.5 pb-0.5' : undefined
                        "
                      >
                        <p class="font-medium">
                          {{ member.productName || member.productSku }}
                        </p>
                        <p
                          v-if="member.productName && member.productSku"
                          class="text-muted-foreground text-xs"
                        >
                          {{ member.productSku }}
                        </p>
                      </div>
                    </div>
                  </TooltipContent>
                </Tooltip>
              </div>
            </ContextMenuTrigger>
            <ContextMenuContent v-if="getDuplicateUrl" class="w-56">
              <ContextMenuLabel class="min-w-0 overflow-hidden font-normal">
                <div class="flex min-w-0 flex-col gap-0.5">
                  <span
                    class="block truncate text-sm font-medium text-foreground"
                  >
                    {{ member.productName || member.productSku }}
                  </span>
                  <span
                    v-if="member.productName && member.productSku"
                    class="block truncate text-xs font-normal text-muted-foreground"
                  >
                    {{ member.productSku }}
                  </span>
                </div>
              </ContextMenuLabel>
              <ContextMenuSeparator />
              <ContextMenuGroup>
                <ContextMenuItem
                  @click="$router.push(getDuplicateUrl(member.productId))"
                >
                  <Copy />
                  {{ t("products.productGroupSidebar.duplicate") }}
                </ContextMenuItem>
              </ContextMenuGroup>
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
