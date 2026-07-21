<script setup>
defineProps({
  method: { type: String, default: "" },
});
</script>

<template>
  <code class="text-xs font-medium">{{ method }}</code>
</template>
