<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed } from "vue";
import { useStore } from "vuex";
import {
  Sparkles,
  Activity,
} from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { t } from "@/i18n";

const store = useStore();
const isAiOpen    = computed(() => store.state.aiChat.isOpen);
const isAiLoading = computed(() => store.state.aiChat.isLoading);
const activeCount = computed(() => store.getters["operations/activeCount"]);

function toggleAi()  { store.commit("aiChat/toggle"); }
function toggleOps() { store.dispatch("operations/togglePanel"); }
</script>

<template>
  <div
    class="flex-none sticky top-0 h-screen border-l bg-background flex flex-col items-center justify-end pb-5 gap-3 overflow-hidden transition-[width] duration-200 ease-in-out"
    :style="{ width: isAiOpen ? '0px' : '52px' }"
  >
    <Button
      size="icon"
      class="relative size-10 rounded-full shadow-md shrink-0"
      :title="t('components.rightActionStrip.backgroundJobsTitle')"
      @click="toggleOps"
    >
      <Activity class="size-4" />
      <span
        v-if="activeCount > 0"
        class="absolute -top-0.5 -right-0.5 size-4 rounded-full bg-destructive text-destructive-foreground text-[9px] font-bold flex items-center justify-center"
      >{{ activeCount > 9 ? "9+" : activeCount }}</span>
      <span v-if="activeCount > 0" class="absolute inset-0 rounded-full bg-primary/25 animate-ping" />
    </Button>

    <Button
      size="icon"
      class="relative size-10 rounded-full shadow-md shrink-0"
      :title="t('components.rightActionStrip.aiAssistantTitle')"
      @click="toggleAi"
    >
      <Spinner v-if="isAiLoading" />
      <Sparkles v-else class="size-4" />
      <span v-if="isAiLoading" class="absolute inset-0 rounded-full bg-primary/25 animate-ping" />
    </Button>
  </div>
</template>