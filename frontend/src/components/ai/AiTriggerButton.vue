<script setup>
import { computed } from "vue";
import { useStore } from "vuex";
import { Sparkles, Loader2 } from "lucide-vue-next";

const store = useStore();
const isLoading = computed(() => store.state.aiChat.isLoading);
const isOpen = computed(() => store.state.aiChat.isOpen);
const panelWidth = computed(() => store.state.aiChat.panelWidth);
const fabsHidden = computed(() => store.state.aiChat.fabsHidden);
const fabRight = computed(() => isOpen.value ? panelWidth.value + 24 : 24);

function toggle() {
  store.commit("aiChat/toggle");
}
</script>

<template>
  <Teleport to="body">
    <button
      v-if="!fabsHidden"
      type="button"
      class="fixed bottom-20 z-[9994] size-12 rounded-full bg-primary text-primary-foreground shadow-lg flex items-center justify-center cursor-pointer hover:bg-primary/90 transition-[right] duration-200 ease-in-out"
      :style="{ right: fabRight + 'px' }"
      :title="isOpen ? 'Закрыть AI Ассистент' : 'AI Ассистент'"
      @click="toggle"
    >
      <Loader2 v-if="isLoading" class="size-5 animate-spin" />
      <Sparkles v-else class="size-5" />

      <span
        v-if="isLoading"
        class="absolute inset-0 rounded-full bg-primary/30 animate-ping"
      />
    </button>
  </Teleport>
</template>
