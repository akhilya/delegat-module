<script setup>
import { computed } from "vue";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import { EMPTY_PROMPT_VISIBLE } from "@/lib/ai/emptyPrompts";

const props = defineProps({
  title: { type: String, required: true },
  description: { type: String, default: "" },
  prompts: { type: Array, default: () => [] },
});

const emit = defineEmits(["pick"]);

const visiblePrompts = computed(() =>
  props.prompts
    .filter((p) => typeof p === "string" && p.trim())
    .slice(0, EMPTY_PROMPT_VISIBLE),
);
</script>

<template>
  <div class="flex min-h-full flex-col" data-slot="ai-chat-empty">
    <div
      class="flex flex-1 flex-col items-center justify-center px-2 py-8 text-center"
    >
      <h2
        class="max-w-[16rem] text-[1.375rem] font-medium leading-tight tracking-tight"
      >
        {{ title }}
      </h2>
      <p
        v-if="description"
        class="mt-2 max-w-[18rem] text-sm leading-relaxed text-muted-foreground"
      >
        {{ description }}
      </p>
    </div>
    <InsetGroup
      v-if="visiblePrompts.length"
      :key="visiblePrompts.join('|')"
      class="w-full shrink-0 motion-safe:animate-in motion-safe:fade-in-0 motion-safe:duration-[var(--duration-ui)] motion-safe:ease-[var(--ease-out-quart)]"
    >
      <InsetGroupItem
        v-for="prompt in visiblePrompts"
        :key="prompt"
        show-chevron
        class="min-h-11 py-3"
        @click="emit('pick', prompt)"
      >
        <span class="text-[0.9375rem] leading-snug">{{ prompt }}</span>
      </InsetGroupItem>
    </InsetGroup>
  </div>
</template>
