<script setup>
defineProps({
  code: { type: String, required: true },
});
</script>

<template>
  <span class="locale-flag" aria-hidden="true">
    <svg v-if="code === 'ru'" viewBox="0 0 9 6" class="size-full">
      <rect width="9" height="2" y="0" fill="#fff" />
      <rect width="9" height="2" y="2" fill="#0039A6" />
      <rect width="9" height="2" y="4" fill="#D52B1E" />
    </svg>
    <svg v-else viewBox="0 0 60 30" class="size-full">
      <rect width="60" height="30" fill="#012169" />
      <path d="M0,0 L60,30 M60,0 L0,30" stroke="#fff" stroke-width="6" />
      <path d="M0,0 L60,30 M60,0 L0,30" stroke="#C8102E" stroke-width="2" />
      <path d="M30,0 V30 M0,15 H60" stroke="#fff" stroke-width="10" />
      <path d="M30,0 V30 M0,15 H60" stroke="#C8102E" stroke-width="6" />
    </svg>
  </span>
</template>

<style scoped>
.locale-flag {
  display: inline-flex;
  width: 1.05rem;
  height: 0.79rem;
  flex-shrink: 0;
  overflow: hidden;
  border-radius: 1px;
  box-shadow: inset 0 0 0 1px rgb(0 0 0 / 12%);
}

.locale-flag svg {
  display: block;
}
</style>
