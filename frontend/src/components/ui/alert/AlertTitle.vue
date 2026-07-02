<script setup>
import { computed } from 'vue'
import { cn } from '@/lib/utils'

const props = defineProps({
  class: {
    type: String,
    default: ''
  }
})

const classes = computed(() => cn('mb-1 font-medium leading-none tracking-tight', props.class))
</script>

<template>
  <h5 :class="classes">
    <slot />
  </h5>
</template>
