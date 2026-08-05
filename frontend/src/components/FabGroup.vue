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

const props = defineProps({
  aiEnabled: { type: Boolean, default: false },
});

const store = useStore();

const isAiOpen = computed(() => store.state.aiChat?.isOpen);
const isAiLoading = computed(() => store.state.aiChat?.isLoading);
const activeCount = computed(() => store.getters["operations/activeCount"]);

function toggleAi() {
  store.commit("aiChat/toggle");
}
function toggleOps() {
  store.dispatch("operations/togglePanel");
}
</script>

<template>
  <Teleport to="body">
    <Transition
      enter-active-class="transition-all duration-200 ease-out"
      enter-from-class="opacity-0 translate-y-2"
      enter-to-class="opacity-100 translate-y-0"
      leave-active-class="transition-all duration-150 ease-in"
      leave-from-class="opacity-100 translate-y-0"
      leave-to-class="opacity-0 translate-y-2"
    >
      <div
        v-if="!isAiOpen"
        class="fixed bottom-6 right-4 z-[9996] flex flex-col-reverse gap-2.5"
      >
        <Button
          v-if="aiEnabled"
          size="icon"
          class="relative size-12 rounded-full shadow-lg shrink-0"
          :title="t('components.fabGroup.aiAssistantTitle')"
          @click="toggleAi"
        >
          <Spinner v-if="isAiLoading" size="lg" />
          <Sparkles v-else class="size-5" />
          <span
            v-if="isAiLoading"
            class="absolute inset-0 rounded-full bg-white/10 animate-pulse pointer-events-none"
          />
        </Button>

        <Button
          size="icon"
          class="relative size-12 rounded-full shadow-lg shrink-0"
          :title="t('components.fabGroup.backgroundJobsTitle')"
          @click="toggleOps"
        >
          <Activity class="size-5" />
          <span
            v-if="activeCount > 0"
            class="absolute -top-0.5 -right-0.5 min-w-[18px] h-[18px] px-1 rounded-full
                   bg-destructive text-destructive-foreground text-[9px] font-bold
                   leading-none flex items-center justify-center"
          >
            {{ activeCount > 9 ? "9+" : activeCount }}
          </span>
        </Button>
      </div>
    </Transition>
  </Teleport>
</template>
