<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed } from "vue";
import { useStore } from "vuex";
import { Sparkles, Activity, CircleHelp } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { t } from "@/i18n";
import { useCompanionSheets } from "@/composables/useCompanionSheets";

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
const { fabRight } = useCompanionSheets();

const isNavbar = computed(() => props.placement === "navbar");
const isAiLoading = computed(() => store.state.aiChat?.isLoading);
const aiOpen = computed(() => !!store.state.aiChat?.isOpen);
const opsOpen = computed(() => !!store.state.operations?.panelOpen);
const docsOpen = computed(() => !!store.state.docs?.panelOpen);
const activeCount = computed(() => store.getters["operations/activeCount"]);
const tooltipSide = computed(() => (isNavbar.value ? "bottom" : "left"));

function toggleAi() {
  store.commit("aiChat/toggle");
}
function toggleOps() {
  store.dispatch("operations/togglePanel");
}
function toggleDocs() {
  store.dispatch("docs/togglePanel");
}

function floatVariant(open) {
  return open ? "outline" : "default";
}
function navVariant(open) {
  return open ? "secondary" : "ghost";
}
</script>

<template>
  <Teleport v-if="!isNavbar" to="body">
    <TooltipProvider :delay-duration="300">
      <div
        data-fab-group
        class="fixed bottom-6 z-[9996] hidden flex-col-reverse gap-2.5 lg:flex"
        :style="{
          right: `${fabRight}px`,
          transition:
            'right var(--duration-overlay) var(--ease-out-quart)',
        }"
      >
        <Tooltip v-if="aiEnabled">
          <TooltipTrigger as-child>
            <Button
              size="icon"
              class="relative size-12 shrink-0 rounded-full shadow-lg"
              :variant="floatVariant(aiOpen)"
              :data-companion-open="aiOpen ? '' : undefined"
              :aria-pressed="aiOpen"
              :aria-label="t('components.fabGroup.aiAssistantTitle')"
              @click="toggleAi"
            >
              <Spinner v-if="isAiLoading" size="lg" />
              <Sparkles v-else class="size-5" />
              <span
                v-if="isAiLoading"
                class="pointer-events-none absolute inset-0 animate-pulse rounded-full bg-white/10"
              />
            </Button>
          </TooltipTrigger>
          <TooltipContent :side="tooltipSide">
            {{ t("components.fabGroup.aiAssistantTitle") }}
          </TooltipContent>
        </Tooltip>

        <Tooltip>
          <TooltipTrigger as-child>
            <Button
              size="icon"
              class="relative size-12 shrink-0 rounded-full shadow-lg"
              :variant="floatVariant(opsOpen)"
              :data-companion-open="opsOpen ? '' : undefined"
              :aria-pressed="opsOpen"
              :aria-label="t('components.fabGroup.backgroundJobsTitle')"
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
          </TooltipTrigger>
          <TooltipContent :side="tooltipSide">
            {{ t("components.fabGroup.backgroundJobsTitle") }}
          </TooltipContent>
        </Tooltip>

        <Tooltip>
          <TooltipTrigger as-child>
            <Button
              size="icon"
              class="relative size-12 shrink-0 rounded-full shadow-lg"
              :variant="floatVariant(docsOpen)"
              :data-companion-open="docsOpen ? '' : undefined"
              :aria-pressed="docsOpen"
              :aria-label="t('components.fabGroup.documentationTitle')"
              @click="toggleDocs"
            >
              <CircleHelp class="size-5" />
            </Button>
          </TooltipTrigger>
          <TooltipContent :side="tooltipSide">
            {{ t("components.fabGroup.documentationTitle") }}
          </TooltipContent>
        </Tooltip>
      </div>
    </TooltipProvider>
  </Teleport>

  <TooltipProvider v-else :delay-duration="300">
    <div data-fab-group class="flex items-center gap-0.5 lg:hidden">
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            size="icon"
            class="relative size-9 shrink-0"
            :variant="navVariant(opsOpen)"
            :data-companion-open="opsOpen ? '' : undefined"
            :aria-pressed="opsOpen"
            :aria-label="t('components.fabGroup.backgroundJobsTitle')"
            @click="toggleOps"
          >
            <Activity class="size-4" />
            <span
              v-if="activeCount > 0"
              class="absolute top-1 right-1 size-1.5 rounded-full bg-destructive"
            />
          </Button>
        </TooltipTrigger>
        <TooltipContent :side="tooltipSide">
          {{ t("components.fabGroup.backgroundJobsTitle") }}
        </TooltipContent>
      </Tooltip>
      <Tooltip v-if="aiEnabled">
        <TooltipTrigger as-child>
          <Button
            size="icon"
            class="relative size-9 shrink-0"
            :variant="navVariant(aiOpen)"
            :data-companion-open="aiOpen ? '' : undefined"
            :aria-pressed="aiOpen"
            :aria-label="t('components.fabGroup.aiAssistantTitle')"
            @click="toggleAi"
          >
            <Spinner v-if="isAiLoading" size="sm" />
            <Sparkles v-else class="size-4" />
          </Button>
        </TooltipTrigger>
        <TooltipContent :side="tooltipSide">
          {{ t("components.fabGroup.aiAssistantTitle") }}
        </TooltipContent>
      </Tooltip>
    </div>
  </TooltipProvider>
</template>
