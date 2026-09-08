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
  if (action.url) router.push(action.url);
  if (action.prompt) emit("ask", action.prompt);
}
</script>

<template>
  <div
    v-if="actions.length"
    class="flex w-full min-w-0 flex-wrap gap-2"
  >
    <Button
      v-for="(action, i) in actions"
      :key="i + action.label"
      variant="outline"
      size="sm"
      class="max-w-full"
      @click="run(action)"
    >
      <ExternalLink v-if="action.url" />
      <MessageSquare v-else />
      <span class="truncate">{{ action.label }}</span>
    </Button>
  </div>
</template>
