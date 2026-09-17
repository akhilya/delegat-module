<script setup>
import { cn } from "@/lib/utils";
import "@/styles/markdown-prose.css";

defineOptions({ inheritAttrs: false });

defineProps({
  html: { type: String, default: "" },
  /** article = docs sheet. compact = assistant. muted = thoughts (inherit color). */
  density: {
    type: String,
    default: "article",
    validator: (value) => ["article", "compact", "muted"].includes(value),
  },
  class: { type: null, required: false },
});
</script>

<template>
  <div
    data-slot="markdown-prose"
    v-bind="$attrs"
    :class="
      cn(
        'markdown-prose',
        density !== 'article' && 'markdown-prose--compact',
        density === 'muted' && 'markdown-prose--muted',
        $props.class,
      )
    "
    v-html="html"
  />
</template>
