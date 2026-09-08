<script setup>
import { Check, X } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import { useI18n } from "vue-i18n";

defineProps({
  plan: { type: Object, required: true },
  confirmed: { type: Boolean, default: false },
  cancelled: { type: Boolean, default: false },
  executed: { type: Boolean, default: false },
  failed: { type: Boolean, default: false },
});
defineEmits(["confirm", "cancel"]);

const { t } = useI18n();
</script>

<template>
  <InsetGroup class="w-full">
    <div class="px-2.5 py-2.5">
      <p class="text-xs text-muted-foreground">{{ t("ai.planTitle") }}</p>
      <p class="mt-0.5 text-sm leading-snug">{{ plan.summary }}</p>
      <p v-if="plan.affectedCount" class="mt-1 text-xs text-muted-foreground">
        {{ t("ai.planAffected") }}
        <span class="text-foreground">{{ plan.affectedCount }}</span>
      </p>
    </div>
    <InsetGroupItem v-for="(step, i) in plan.steps" :key="i" as="div">
      <p class="leading-snug">{{ step.description }}</p>
    </InsetGroupItem>
    <div v-if="!confirmed && !cancelled" class="flex gap-2 px-2 pb-2 pt-1">
      <Button size="sm" class="h-8 flex-1" @click="$emit('confirm')">
        {{ t("ai.planExecute") }}
      </Button>
      <Button
        variant="ghost"
        size="sm"
        class="h-8 flex-1"
        @click="$emit('cancel')"
      >
        {{ t("common.cancel") }}
      </Button>
    </div>
    <div
      v-else
      class="flex items-center gap-2 px-2.5 pb-2.5 text-xs text-muted-foreground"
    >
      <Check v-if="confirmed && !cancelled && !failed" class="size-3.5" />
      <X v-else class="size-3.5" :class="failed && 'text-destructive'" />
      <span :class="failed && 'text-destructive'">{{
        cancelled
          ? t("ai.planCancelled")
          : failed
            ? t("ai.planFailed")
            : executed
              ? t("ai.planExecuted")
              : t("ai.planExecuting")
      }}</span>
    </div>
  </InsetGroup>
</template>
