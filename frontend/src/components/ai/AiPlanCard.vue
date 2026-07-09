<script setup>
import { AlertTriangle, CheckCircle2, X } from "lucide-vue-next";
import Button from "@/components/ui/button/Button.vue";

defineProps({
  plan: { type: Object, required: true },
  confirmed: { type: Boolean, default: false },
  cancelled: { type: Boolean, default: false },
});
defineEmits(["confirm", "cancel"]);
</script>

<template>
  <div
    class="border-l-2 pl-3 py-0.5 text-sm"
    :class="plan.isDestructive ? 'border-amber-400/70' : 'border-border'"
  >
    <!-- Header -->
    <div class="flex items-center gap-1.5 text-xs font-medium text-muted-foreground mb-1.5">
      <AlertTriangle v-if="plan.isDestructive" class="size-3.5 text-amber-500 shrink-0" />
      <span>{{ plan.summary }}</span>
    </div>

    <!-- Steps -->
    <div v-if="plan.steps?.length" class="flex flex-col gap-0.5 mb-2">
      <div
        v-for="(step, i) in plan.steps"
        :key="i"
        class="flex items-baseline gap-2 text-xs text-muted-foreground"
      >
        <span class="font-mono text-[10px] text-foreground/50 shrink-0 min-w-[60px]">{{ step.tool }}</span>
        <span class="leading-snug">{{ step.description }}</span>
      </div>
    </div>

    <!-- Affected count -->
    <div v-if="plan.affectedCount" class="text-xs text-muted-foreground mb-2">
      Затронуто: <span class="text-foreground font-medium">{{ plan.affectedCount }}</span>
    </div>

    <!-- Actions / Status -->
    <div v-if="!confirmed && !cancelled" class="flex gap-2 mt-2">
      <Button size="sm" class="h-7 text-xs" @click="$emit('confirm')">Выполнить</Button>
      <Button variant="ghost" size="sm" class="h-7 text-xs" @click="$emit('cancel')">Отмена</Button>
    </div>
    <div v-else-if="confirmed" class="flex items-center gap-1 text-xs text-muted-foreground mt-1">
      <CheckCircle2 class="size-3 text-green-600" />
      <span>Выполняю…</span>
    </div>
    <div v-else-if="cancelled" class="flex items-center gap-1 text-xs text-muted-foreground mt-1">
      <X class="size-3" />
      <span>Отменено</span>
    </div>
  </div>
</template>
