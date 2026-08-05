<script setup>
import { ref, nextTick } from "vue";
import { Plus, Copy } from "lucide-vue-next";
import { t } from "@/i18n";
import { Loader2 } from "lucide-vue-next";
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuTrigger,
} from "@/components/ui/context-menu";

const props = defineProps({
  group: { type: Object, default: null },
  loading: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
  addUrl: { type: String, default: null },
  tab: { type: String, default: null },
  getDuplicateUrl: { type: Function, default: null },
});

const scrollRef = ref(null);

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

defineExpose({ scrollCurrentIntoView, scrollRef });
</script>

<template>
  <!-- Always reserve column width on lg+ to avoid layout flicker while group loads. -->
  <div
    class="product-group-sidebar hidden lg:flex shrink-0 flex-col self-start overflow-hidden rounded-xl border bg-card text-card-foreground sticky top-14 h-[calc(100svh-3.5rem-1rem-4.75rem)]"
  >
    <div
      v-if="loading"
      class="flex min-h-0 flex-1 items-center justify-center py-6"
    >
      <Loader2 class="size-5 animate-spin text-muted-foreground" />
    </div>

    <div
      v-else
      ref="scrollRef"
      class="thin-scrollbar min-h-0 flex-1 space-y-1.5 overflow-y-auto p-3"
      :class="{ 'pointer-events-none opacity-50': disabled }"
    >
      <router-link
        v-if="addUrl"
        :to="addUrl"
        class="flex h-14 items-center justify-center rounded-lg border border-dashed p-2 transition-colors hover:bg-accent/50"
      >
        <Plus class="size-4 text-muted-foreground" />
      </router-link>
      <div
        v-else-if="group"
        class="flex h-14 items-center justify-center rounded-lg bg-accent p-2"
      >
        <Plus class="size-4 text-muted-foreground" />
      </div>

      <template v-if="group">
        <ContextMenu v-for="member in group.members" :key="member.productId">
          <ContextMenuTrigger as-child>
            <router-link
              :to="getMemberRoute(member)"
              class="relative flex items-center gap-2.5 rounded-lg p-2 transition-colors"
              :class="member.isCurrent ? 'bg-accent' : 'hover:bg-accent/50'"
              v-bind="member.isCurrent ? { 'data-current-member': '' } : {}"
            >
              <span
                v-if="member.isCurrent"
                class="absolute top-2 bottom-2 left-0 w-[3px] rounded-full bg-foreground"
              />
              <img
                v-if="member.imageThumbnailUrl || member.imageUrl"
                :src="member.imageThumbnailUrl ?? member.imageUrl"
                class="size-10 shrink-0 rounded-md object-cover"
                :alt="member.productName"
              />
              <div v-else class="size-10 shrink-0 rounded-md bg-muted" />
              <div class="min-w-0">
                <p
                  class="truncate text-sm"
                  :class="member.isCurrent ? 'font-medium' : ''"
                >
                  {{ member.productName || member.productSku }}
                </p>
                <p class="truncate text-xs text-muted-foreground">
                  {{ member.productSku }}
                </p>
              </div>
            </router-link>
          </ContextMenuTrigger>
          <ContextMenuContent v-if="getDuplicateUrl">
            <ContextMenuItem @click="$router.push(getDuplicateUrl(member.productId))">
              <Copy class="size-4 mr-2" />
              {{ t("products.productGroupSidebar.duplicate") }}
            </ContextMenuItem>
          </ContextMenuContent>
        </ContextMenu>
      </template>
    </div>
  </div>
</template>

<style scoped>
.product-group-sidebar {
  width: clamp(14rem, 22vw, 24rem);
}
</style>
