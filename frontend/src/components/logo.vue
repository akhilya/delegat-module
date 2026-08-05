<script setup>
import { cn } from "@/lib/utils";
import { brandLogo, brandName } from "@/brand";
</script>

<template>
  <img
    v-if="brandLogo"
    :src="brandLogo"
    :alt="brandName"
    :class="cn('w-16 opacity-80', $attrs.class)"
  />
</template>
