<script setup>
import { cn } from "@/lib/utils";
import { BRAND_LOGO, BRAND_NAME } from "@/brand";
</script>

<template>
  <img
    :src="BRAND_LOGO"
    :alt="BRAND_NAME"
    :class="cn('w-16 opacity-80', $attrs.class)"
  />
</template>
