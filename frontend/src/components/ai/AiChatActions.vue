<script setup>
import { ExternalLink, MessageSquare } from "lucide-vue-next";
import { useRouter } from "vue-router";
import { Button } from "@/components/ui/button";

defineProps({
  actions: { type: Array, default: () => [] },
});

const emit = defineEmits(["ask"]);
const router = useRouter();

function run(action) {
  if (action.kind === "open" || action.url) {
    if (action.url) router.push(action.url);
    return;
  }
  if (action.prompt) emit("ask", action.prompt);
}
</script>

<template>
  <div
    v-if="actions.length"
    class="flex w-full min-w-0 flex-col gap-2"
  >
    <Button
      v-for="(action, i) in actions"
      :key="i + action.label"
      :variant="action.kind === 'continue' ? 'secondary' : 'outline'"
      size="sm"
      class="h-auto max-w-full flex-col items-start gap-0.5 whitespace-normal py-2 text-left"
      :aria-label="
        action.kind === 'continue'
          ? action.prompt
          : action.url
      "
      @click="run(action)"
    >
      <span class="flex min-w-0 items-center gap-1.5">
        <MessageSquare v-if="action.kind === 'continue'" class="size-3.5 shrink-0" />
        <ExternalLink v-else class="size-3.5 shrink-0" />
        <span class="font-medium">{{ action.label }}</span>
      </span>
      <span
        v-if="action.kind === 'continue' ? action.prompt : action.url"
        class="pl-5 text-xs font-normal text-muted-foreground wrap-break-word"
      >
        {{ action.kind === "continue" ? action.prompt : action.url }}
      </span>
    </Button>
  </div>
</template>
