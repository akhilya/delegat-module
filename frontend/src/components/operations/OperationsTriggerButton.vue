<script setup>
import { computed } from "vue";
import { useStore } from "vuex";
import { Activity, ChevronRight, ChevronLeft } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { t } from "@/i18n";

const store = useStore();

const activeCount = computed(() => store.getters["operations/activeCount"]);
const aiPanelOpen = computed(() => store.state.aiChat.isOpen);
const aiPanelWidth = computed(() => store.state.aiChat.panelWidth);
const fabsHidden = computed(() => store.state.aiChat.fabsHidden);
const fabRight = computed(() => aiPanelOpen.value ? aiPanelWidth.value + 24 : 24);

function toggle() {
  store.dispatch("operations/togglePanel");
}
function hideFabs() {
  store.commit("aiChat/setFabsHidden", true);
}
function showFabs() {
  store.commit("aiChat/setFabsHidden", false);
}
</script>

<template>
  <Teleport to="body">
    <!-- Restore pill — visible only when FABs are hidden -->
    <Button
      v-if="fabsHidden"
      type="button"
      variant="ghost"
      class="fixed bottom-6 right-0 z-[9996] h-12 w-5 rounded-l-xl rounded-r-none bg-muted/80 border border-r-0 border-border/60 backdrop-blur-sm text-muted-foreground hover:text-foreground hover:bg-muted"
      :title="t('operations.showButtons')"
      @click="showFabs"
    >
      <ChevronLeft class="size-3" />
    </Button>

    <!-- FAB group with collapse toggle on hover -->
    <div
      v-else
      class="fixed bottom-6 z-[9996] flex items-center gap-1.5 group/opsgroup transition-[right] duration-200 ease-in-out"
      :style="{ right: fabRight + 'px' }"
    >
      <!-- Collapse chevron: appears left of button on hover -->
      <Button
        type="button"
        variant="ghost"
        size="icon"
        class="size-6 rounded-full bg-background/90 border border-border/60 backdrop-blur-sm text-muted-foreground opacity-0 group-hover/opsgroup:opacity-100 transition-opacity hover:text-foreground shrink-0"
        :title="t('operations.hideButtons')"
        @click.stop="hideFabs"
      >
        <ChevronRight class="size-3" />
      </Button>

      <!-- Main operations button -->
      <Button
        type="button"
        size="icon"
        class="relative size-12 rounded-full shadow-lg shrink-0"
        @click="toggle"
      >
        <Activity class="size-5" />

        <!-- Badge -->
        <span
          v-if="activeCount > 0"
          class="absolute -top-1 -right-1 size-5 rounded-full bg-error text-error-foreground text-[10px] font-bold flex items-center justify-center pointer-events-none"
        >
          {{ activeCount > 9 ? "9+" : activeCount }}
        </span>

        <!-- Pulse ring -->
        <span
          v-if="activeCount > 0"
          class="absolute inset-0 rounded-full bg-primary/30 animate-ping pointer-events-none"
        />
      </Button>
    </div>
  </Teleport>
</template>