<script setup>
import { ref, nextTick } from "vue";
import { Plus, Copy } from "lucide-vue-next";
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
  <div
    v-if="group"
    class="w-96 shrink-0 hidden lg:flex flex-col bg-card text-card-foreground border rounded-xl overflow-hidden sticky top-14 self-start h-[calc(100svh-3.5rem-1rem-4.75rem)]"
  >
    <div v-if="loading" class="flex-1 flex items-center justify-center min-h-24 py-6">
      <Loader2 class="size-5 animate-spin text-muted-foreground" />
    </div>

    <template v-else>
      <div class="shrink-0 border-b px-4 py-3">
        <p class="text-sm font-medium">Товары в группе</p>
        <p class="text-xs text-muted-foreground mt-0.5 truncate">
          {{ group.name }}
        </p>
      </div>

      <div
        ref="scrollRef"
        class="flex-1 min-h-0 overflow-y-auto thin-scrollbar p-3 space-y-1.5"
        :class="{ 'pointer-events-none opacity-50': disabled }"
      >
        <router-link
          v-if="addUrl"
          :to="addUrl"
          class="flex items-center justify-center p-2 h-14 rounded-lg border border-dashed hover:bg-accent/50 transition-colors"
        >
          <Plus class="size-4 text-muted-foreground" />
        </router-link>
        <div
          v-else
          class="flex items-center justify-center p-2 h-14 rounded-lg bg-accent"
        >
          <Plus class="size-4 text-muted-foreground" />
        </div>

        <ContextMenu v-for="member in group.members" :key="member.productId">
          <ContextMenuTrigger as-child>
            <router-link
              :to="getMemberRoute(member)"
              class="flex items-center gap-2.5 p-2 rounded-lg transition-colors relative"
              :class="member.isCurrent ? 'bg-accent' : 'hover:bg-accent/50'"
              v-bind="member.isCurrent ? { 'data-current-member': '' } : {}"
            >
              <span
                v-if="member.isCurrent"
                class="absolute left-0 top-2 bottom-2 w-[3px] rounded-full bg-foreground"
              />
              <img
                v-if="member.imageThumbnailUrl || member.imageUrl"
                :src="member.imageThumbnailUrl ?? member.imageUrl"
                class="size-10 rounded-md object-cover shrink-0"
                :alt="member.productName"
              />
              <div
                v-else
                class="size-10 rounded-md bg-muted shrink-0"
              />
              <div class="min-w-0">
                <p
                  class="text-sm truncate"
                  :class="member.isCurrent ? 'font-medium' : ''"
                >
                  {{ member.productName || member.productSku }}
                </p>
                <p class="text-xs text-muted-foreground truncate">
                  {{ member.productSku }}
                </p>
              </div>
            </router-link>
          </ContextMenuTrigger>
          <ContextMenuContent v-if="getDuplicateUrl">
            <ContextMenuItem @click="$router.push(getDuplicateUrl(member.productId))">
              <Copy class="size-4 mr-2" />
              Дублировать
            </ContextMenuItem>
          </ContextMenuContent>
        </ContextMenu>
      </div>
    </template>
  </div>
</template>