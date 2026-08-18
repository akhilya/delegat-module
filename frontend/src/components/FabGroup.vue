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
  /** float = corner FABs (lg+). navbar = compact icons in the top bar (< lg). */
  placement: {
    type: String,
    default: "float",
    validator: (value) => ["float", "navbar"].includes(value),
  },
});

const store = useStore();

const isNavbar = computed(() => props.placement === "navbar");
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
  <Teleport v-if="!isNavbar" to="body">
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
        class="fixed bottom-6 right-4 z-[9996] hidden flex-col-reverse gap-2.5 lg:flex"
      >
        <Button
          v-if="aiEnabled"
          size="icon"
          class="relative size-12 shrink-0 rounded-full shadow-lg"
          :title="t('components.fabGroup.aiAssistantTitle')"
          @click="toggleAi"
        >
          <Spinner v-if="isAiLoading" size="lg" />
          <Sparkles v-else class="size-5" />
          <span
            v-if="isAiLoading"
            class="pointer-events-none absolute inset-0 animate-pulse rounded-full bg-white/10"
          />
        </Button>

        <Button
          size="icon"
          class="relative size-12 shrink-0 rounded-full shadow-lg"
          :title="t('components.fabGroup.backgroundJobsTitle')"
          @click="toggleOps"
        >
          <Activity class="size-5" />
          <span
            v-if="activeCount > 0"
            class="absolute -top-0.5 -right-0.5 flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-destructive px-1 text-[9px] font-bold leading-none text-destructive-foreground"
          >
            {{ activeCount > 9 ? "9+" : activeCount }}
          </span>
        </Button>
      </div>
    </Transition>
  </Teleport>

  <div v-else class="flex items-center gap-0.5 lg:hidden">
    <Button
      variant="ghost"
      size="icon"
      class="relative size-9 shrink-0"
      :title="t('components.fabGroup.backgroundJobsTitle')"
      @click="toggleOps"
    >
      <Activity class="size-4" />
      <span
        v-if="activeCount > 0"
        class="absolute top-1 right-1 size-1.5 rounded-full bg-destructive"
      />
    </Button>
    <Button
      v-if="aiEnabled"
      variant="ghost"
      size="icon"
      class="relative size-9 shrink-0"
      :title="t('components.fabGroup.aiAssistantTitle')"
      @click="toggleAi"
    >
      <Spinner v-if="isAiLoading" size="sm" />
      <Sparkles v-else class="size-4" />
    </Button>
  </div>
</template>
