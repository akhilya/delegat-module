<script setup>
import { Spinner } from "@/components/ui/spinner";
import { Check } from "lucide-vue-next";
import "./aiMessage.css";

defineProps({
  running: { type: Boolean, default: false },
});
</script>

<template>
  <span class="relative inline-grid size-3.5 shrink-0 place-items-center">
    <Transition name="ai-fade">
      <Spinner
        v-if="running"
        key="running"
        size="xs"
        class="col-start-1 row-start-1"
      />
      <Check
        v-else
        key="done"
        class="col-start-1 row-start-1 size-3.5 text-muted-foreground"
      />
    </Transition>
  </span>
</template>
