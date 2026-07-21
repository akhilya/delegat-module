<script setup>
defineProps({
  path: { type: String, default: "" },
  title: { type: String, default: "" },
});
</script>

<template>
  <div
    class="truncate font-mono text-xs"
    :title="title || path"
  >
    {{ path }}
  </div>
</template>
