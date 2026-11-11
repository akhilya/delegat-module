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

defineProps({
  aiEnabled: { type: Boolean, default: false },
});

const store = useStore();

const isAiLoading = computed(() => store.state.aiChat?.isLoading);
const aiOpen = computed(() => !!store.state.aiChat?.isOpen);
const opsOpen = computed(() => !!store.state.operations?.panelOpen);
const docsOpen = computed(() => !!store.state.docs?.panelOpen);
const activeCount = computed(() => store.getters["operations/activeCount"]);

function toggleAi() {
  store.commit("aiChat/toggle");
}
function toggleOps() {
  store.dispatch("operations/togglePanel");
}
function toggleDocs() {
  store.dispatch("docs/togglePanel");
}

function navVariant(open) {
  return open ? "secondary" : "ghost";
}
</script>

<template>
  <TooltipProvider :delay-duration="100">
    <div data-companion-actions class="flex items-center gap-0.5">
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            size="icon"
            class="relative size-9 shrink-0"
            :variant="navVariant(docsOpen)"
            :data-companion-open="docsOpen ? '' : undefined"
            :aria-pressed="docsOpen"
            :aria-label="t('components.fabGroup.documentationTitle')"
            @click="toggleDocs"
          >
            <CircleHelp class="size-4" />
          </Button>
        </TooltipTrigger>
        <TooltipContent side="bottom">
          {{ t("components.fabGroup.documentationTitle") }}
        </TooltipContent>
      </Tooltip>

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
        <TooltipContent side="bottom">
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
        <TooltipContent side="bottom">
          {{ t("components.fabGroup.aiAssistantTitle") }}
        </TooltipContent>
      </Tooltip>
    </div>
  </TooltipProvider>
</template>
