<script>
export default {
  components: {},
  props: {
    oldSKU: String,
    newSKU: String,
  },
};
</script>

<template>
  <div class="flex flex-col items-center">
    <span>
      {{ newSKU }}
    </span>
    <span class="text-muted-foreground text-sm">
      {{ oldSKU }}
    </span>
  </div>
</template>
