<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed } from "vue";
import { useStore } from "vuex";
import { Sparkles } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { useI18n } from "vue-i18n";

const { t } = useI18n();
const store = useStore();
const isLoading = computed(() => store.state.aiChat.isLoading);
const isOpen = computed(() => store.state.aiChat.isOpen);
const panelWidth = computed(() => store.state.aiChat.panelWidth);
const fabsHidden = computed(() => store.state.aiChat.fabsHidden);
const fabRight = computed(() => (isOpen.value ? panelWidth.value + 24 : 24));

function toggle() {
  store.commit("aiChat/toggle");
}
</script>

<template>
  <Teleport to="body">
    <Button
      v-if="!fabsHidden"
      type="button"
      size="icon"
      class="fixed bottom-20 z-[9994] size-12 rounded-full shadow-lg transition-[right] duration-[var(--duration-overlay)] ease-[var(--ease-out-quart)]"
      :style="{ right: fabRight + 'px' }"
      :title="isOpen ? t('ai.closeAssistant') : t('ai.openAssistant')"
      @click="toggle"
    >
      <Spinner v-if="isLoading" size="lg" />
      <Sparkles v-else class="size-5" />

      <span
        v-if="isLoading"
        class="absolute inset-0 rounded-full bg-primary/30 animate-ping pointer-events-none"
      />
    </Button>
  </Teleport>
</template>
