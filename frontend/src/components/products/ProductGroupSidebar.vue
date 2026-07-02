<script setup>
import { ref, nextTick, watch } from 'vue';
import { Layers, ExternalLink, Plus, Copy, ArrowRightLeft } from 'lucide-vue-next';
import { Loader2 } from 'lucide-vue-next';
import Button from '@/components/ui/button/Button.vue';
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuTrigger,
} from '@/components/ui/context-menu';

const props = defineProps({
  /** Group object: { id, name, members: [{ productId, productName, productSku, imageUrl, imageThumbnailUrl, isCurrent? }] } */
  group: { type: Object, default: null },
  /** Show loading spinner instead of content */
  loading: { type: Boolean, default: false },
  /** Disable interactions (e.g. while product is loading/submitting) */
  disabled: { type: Boolean, default: false },
  /** URL for the "+" add/duplicate button. If null, shows a static highlighted box (creation mode) */
  addUrl: { type: String, default: null },
  /** Current tab query param for member links */
  tab: { type: String, default: null },
  /** Show "change group" button in header */
  showChangeGroup: { type: Boolean, default: false },
  /** Function (productId) => url for duplicate context menu */
  getDuplicateUrl: { type: Function, default: null },
});

const emit = defineEmits(['change-group']);

const scrollRef = ref(null);

// Cancel any in-flight smooth scroll before starting a new one
function scrollCurrentIntoView() {
  nextTick(() => {
    const container = scrollRef.value;
    if (!container) return;
    container.scrollTo({ top: container.scrollTop, behavior: 'instant' });
    const currentEl = container.querySelector('[data-current-member]');
    if (currentEl) {
      currentEl.scrollIntoView({ behavior: 'smooth', block: 'center' });
    }
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
    class="w-96 shrink-0 hidden lg:flex flex-col bg-card text-card-foreground border rounded-xl overflow-hidden sticky top-[5.5rem] self-start"
    style="max-height: calc(100dvh - 5.5rem - 2rem)"
  >
    <!-- Loading state -->
    <div v-if="loading && !group" class="flex-1 flex items-center justify-center min-h-24 py-6">
      <Loader2 class="size-5 animate-spin text-muted-foreground" />
    </div>
    <template v-else-if="group">
      <!-- Header -->
      <div class="flex items-center justify-between px-4 h-11 border-b shrink-0">
        <div class="flex items-center gap-2 min-w-0">
          <Layers class="size-4 text-muted-foreground shrink-0" />
          <span class="text-sm font-medium truncate">{{ group.name }}</span>
        </div>
        <div class="flex items-center gap-1 shrink-0">
          <Button
            v-if="showChangeGroup"
            variant="ghost"
            size="icon"
            class="size-7"
            title="Сменить группу"
            aria-label="Сменить группу"
            type="button"
            @click="emit('change-group')"
          >
            <ArrowRightLeft class="size-3.5 text-muted-foreground" />
          </Button>
          <router-link
            :to="`/product-groups/${group.id}/edit`"
            class="inline-flex items-center justify-center size-7 rounded-md hover:bg-accent transition-colors"
            title="Редактировать группу"
            aria-label="Редактировать группу"
          >
            <ExternalLink class="size-3.5 text-muted-foreground" />
          </router-link>
        </div>
      </div>
      <!-- Scrollable product list -->
      <div
        ref="scrollRef"
        class="flex-1 min-h-0 overflow-y-auto thin-scrollbar p-3 space-y-1.5"
        :class="{ 'pointer-events-none opacity-50': disabled }"
      >
        <!-- Add product button -->
        <router-link
          v-if="addUrl"
          :to="addUrl"
          class="flex items-center justify-center p-2 h-16 rounded-lg border border-dashed hover:bg-accent/50 transition-colors"
        >
          <Plus class="size-4 text-muted-foreground" />
        </router-link>
        <div
          v-else
          class="flex items-center justify-center p-2 h-16 rounded-lg bg-accent"
        >
          <Plus class="size-4 text-muted-foreground" />
        </div>

        <ContextMenu v-for="member in group.members" :key="member.productId">
          <ContextMenuTrigger as-child>
            <router-link
              :to="getMemberRoute(member)"
              class="flex items-center gap-3 p-2 rounded-lg transition-colors relative"
              :class="member.isCurrent ? 'bg-accent' : 'hover:bg-accent/50'"
              v-bind="member.isCurrent ? { 'data-current-member': '' } : {}"
            >
              <span v-if="member.isCurrent" class="absolute left-0 top-2 bottom-2 w-[3px] rounded-full bg-foreground" />
              <img
                v-if="member.imageThumbnailUrl || member.imageUrl"
                :src="member.imageThumbnailUrl ?? member.imageUrl"
                class="size-12 rounded-md object-cover shrink-0"
                :alt="member.productName"
              />
              <div v-else class="size-12 rounded-md bg-muted flex items-center justify-center shrink-0">
                <Layers class="size-5 text-muted-foreground" />
              </div>
              <div class="min-w-0">
                <p class="text-sm truncate" :class="member.isCurrent ? 'font-medium' : ''">{{ member.productName || member.productSku }}</p>
                <p class="text-xs text-muted-foreground">{{ member.productSku }}</p>
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

    <!-- Empty state: no group assigned — compact, not a full empty-state splash -->
    <template v-else>
      <button
        type="button"
        class="flex-1 flex items-center justify-center gap-2 p-4 text-sm text-muted-foreground hover:text-foreground hover:bg-muted/40 transition-colors cursor-pointer"
        @click="emit('change-group')"
      >
        <Plus class="size-4" />
        Добавить в группу
      </button>
    </template>
  </div>
</template>
