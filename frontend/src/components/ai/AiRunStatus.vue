<script setup>
import { Spinner } from "@/components/ui/spinner";
import { Check, Square, X } from "lucide-vue-next";
import "./aiMessage.css";

defineProps({
  running: { type: Boolean, default: false },
  stopped: { type: Boolean, default: false },
  failed: { type: Boolean, default: false },
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
      <Square
        v-else-if="stopped"
        key="stopped"
        class="col-start-1 row-start-1 size-3 text-muted-foreground"
      />
      <X
        v-else-if="failed"
        key="failed"
        class="col-start-1 row-start-1 size-3.5 text-destructive"
      />
      <Check
        v-else
        key="done"
        class="col-start-1 row-start-1 size-3.5 text-muted-foreground"
      />
    </Transition>
  </span>
</template>
