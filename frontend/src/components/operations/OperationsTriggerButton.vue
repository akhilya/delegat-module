<script setup>
import { computed } from "vue";
import { useStore } from "vuex";
import { Activity, ChevronRight, ChevronLeft } from "lucide-vue-next";

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
    <button
      v-if="fabsHidden"
      type="button"
      class="fixed bottom-6 right-0 z-[9996] h-12 w-5 rounded-l-xl bg-muted/80 border border-r-0 border-border/60 backdrop-blur-sm text-muted-foreground flex items-center justify-center hover:text-foreground hover:bg-muted transition-colors"
      title="Показать кнопки"
      @click="showFabs"
    >
      <ChevronLeft class="size-3" />
    </button>

    <!-- FAB group with collapse toggle on hover -->
    <div
      v-else
      class="fixed bottom-6 z-[9996] flex items-center gap-1.5 group/opsgroup transition-[right] duration-200 ease-in-out"
      :style="{ right: fabRight + 'px' }"
    >
      <!-- Collapse chevron: appears left of button on hover -->
      <button
        type="button"
        class="size-6 rounded-full bg-background/90 border border-border/60 backdrop-blur-sm text-muted-foreground flex items-center justify-center opacity-0 group-hover/opsgroup:opacity-100 transition-opacity hover:text-foreground shrink-0"
        title="Скрыть кнопки"
        @click.stop="hideFabs"
      >
        <ChevronRight class="size-3" />
      </button>

      <!-- Main operations button -->
      <button
        type="button"
        class="size-12 rounded-full bg-primary text-primary-foreground shadow-lg flex items-center justify-center cursor-pointer hover:bg-primary/90 transition-colors shrink-0"
        @click="toggle"
      >
        <Activity class="size-5" />

        <!-- Badge -->
        <span
          v-if="activeCount > 0"
          class="absolute -top-1 -right-1 size-5 rounded-full bg-error text-error-foreground text-[10px] font-bold flex items-center justify-center"
        >
          {{ activeCount > 9 ? "9+" : activeCount }}
        </span>

        <!-- Pulse ring -->
        <span
          v-if="activeCount > 0"
          class="absolute inset-0 rounded-full bg-primary/30 animate-ping"
        />
      </button>
    </div>
  </Teleport>
</template>
