<script setup>
import { Handle, Position } from "@vue-flow/core";

// A transparent pass-through inserted by double-clicking a wire — drag it to bend the wire's path.
defineProps({
  id: String,
  data: { type: Object, default: () => ({}) },
  selected: Boolean,
});
</script>

<template>
  <div :class="['vf-reroute', selected && 'vf-reroute--selected']">
    <Handle id="in" type="target" :position="Position.Left" />
    <Handle id="out" type="source" :position="Position.Right" />
  </div>
</template>

<style scoped>
.vf-reroute {
  width: 14px;
  height: 14px;
  border-radius: 50%;
  background: var(--muted-foreground);
  border: 2px solid var(--card);
  box-shadow: 0 0 0 1px var(--border);
}
.vf-reroute--selected {
  background: var(--primary);
  box-shadow: 0 0 0 2px var(--primary);
}
</style>
