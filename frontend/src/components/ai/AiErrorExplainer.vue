<script setup>
import { ref } from "vue";
import { http } from "@/main";
import { Loader2, Sparkles, X } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { useI18n } from "vue-i18n";

const { t } = useI18n();

const props = defineProps({
  errorMessage: { type: String, required: true },
  context: { type: String, default: "" },
  productId: { type: Number, default: null },
  connectionId: { type: Number, default: null },
});

const isLoading = ref(false);
const explanation = ref(null);

async function explain() {
  if (isLoading.value) return;
  isLoading.value = true;
  try {
    const r = await http.post("/ai/explain-error", {
      errorMessage: props.errorMessage,
      context: props.context,
      productId: props.productId,
      connectionId: props.connectionId,
    });
    explanation.value = r.data.explanation;
  } catch {
    explanation.value = t("ai.explanationFailed");
  } finally {
    isLoading.value = false;
  }
}
</script>

<template>
  <div class="inline-flex flex-col gap-1.5">
    <Button
      v-if="!explanation && !isLoading"
      variant="ghost"
      size="sm"
      class="h-6 text-xs gap-1.5 px-2 self-start"
      @click="explain"
    >
      <Sparkles class="size-3" />
      {{ t("ai.explainError") }}
    </Button>

    <div v-if="isLoading" class="flex items-center gap-1.5 text-xs text-muted-foreground italic">
      <Loader2 class="size-3 animate-spin" />
      {{ t("ai.analyzing") }}
    </div>

    <div v-if="explanation" class="rounded-lg border bg-muted/50 p-2.5 max-w-sm text-xs">
      <div class="flex items-center justify-between mb-1.5">
        <span class="flex items-center gap-1 font-semibold text-primary text-[11px]">
          <Sparkles class="size-3" />
          {{ t("ai.errorExplanationTitle") }}
        </span>
        <Button variant="ghost" size="icon" class="h-5 w-5" @click="explanation = null">
          <X class="size-3" />
        </Button>
      </div>
      <p class="text-muted-foreground leading-relaxed whitespace-pre-wrap">{{ explanation }}</p>
    </div>
  </div>
</template>
