<script setup>
import { computed } from "vue";
import Kbd from "./Kbd.vue";
import KbdGroup from "./KbdGroup.vue";
import { cn } from "@/lib/utils";
import { modKeyLabel } from "@/composables/useModKey";

const props = defineProps({
  /**
   * Shortcut segments. Special tokens:
   * - `mod` → ⌘ on Apple, Ctrl elsewhere
   * - `shift` → ⇧
   * - `alt` → ⌥ / Alt
   * - `enter` → ↵
   * - `esc` → Esc
   * - `backspace` → ⌫
   * - `delete` / `del` → Del
   * Anything else is shown as-is (e.g. `S`, `F2`).
   */
  keys: {
    type: Array,
    required: true,
  },
  class: { type: null, required: false },
});

function resolveKey(key) {
  const k = String(key);
  switch (k.toLowerCase()) {
    case "mod":
    case "ctrl":
    case "cmd":
    case "meta":
      return modKeyLabel();
    case "shift":
      return "⇧";
    case "alt":
    case "option":
      return modKeyLabel() === "⌘" ? "⌥" : "Alt";
    case "enter":
    case "return":
      return "↵";
    case "esc":
    case "escape":
      return "Esc";
    case "backspace":
      return "⌫";
    case "delete":
    case "del":
      return "Del";
    default:
      return k;
  }
}

const resolved = computed(() => props.keys.map(resolveKey));
</script>

<template>
  <KbdGroup :class="cn(props.class)">
    <Kbd v-for="(label, i) in resolved" :key="`${label}-${i}`">{{ label }}</Kbd>
  </KbdGroup>
</template>
