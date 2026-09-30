<script setup>
import { nextTick, onMounted, ref, watch } from "vue";
import { cn } from "@/lib/utils";
import "@/styles/markdown-prose.css";

defineOptions({ inheritAttrs: false });

const props = defineProps({
  html: { type: String, default: "" },
  /** article = docs sheet. compact = assistant. muted = thoughts (inherit color). */
  density: {
    type: String,
    default: "article",
    validator: (value) => ["article", "compact", "muted"].includes(value),
  },
  class: { type: null, required: false },
});

const root = ref(null);

async function wrapTables() {
  await nextTick();
  const el = root.value;
  if (!el) return;
  for (const table of el.querySelectorAll("table")) {
    table.setAttribute("data-slot", "table");
    if (table.parentElement?.dataset.slot === "table-container") continue;
    const wrap = document.createElement("div");
    wrap.dataset.slot = "table-container";
    wrap.className = "relative w-full overflow-auto";
    table.parentNode?.insertBefore(wrap, table);
    wrap.appendChild(table);
  }
}

onMounted(wrapTables);
watch(() => props.html, wrapTables);
</script>

<template>
  <div
    ref="root"
    data-slot="markdown-prose"
    v-bind="$attrs"
    :class="
      cn(
        'markdown-prose',
        density !== 'article' && 'markdown-prose--compact',
        density === 'muted' && 'markdown-prose--muted',
        props.class,
      )
    "
    v-html="html"
  />
</template>
