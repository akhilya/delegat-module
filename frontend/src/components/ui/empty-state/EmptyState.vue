<script setup>
import { useSlots } from "vue";
import { Loader2 } from "lucide-vue-next";
import {
  Empty,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
  EmptyDescription,
  EmptyContent,
} from "@/components/ui/empty";

defineProps({
  loading: { type: Boolean, default: false },
  title: { type: String, default: "" },
  description: { type: String, default: "" },
  // "sm" for table cells / dense sections, "default" for page-level areas
  size: { type: String, default: "default" },
});

const slots = useSlots();
</script>

<template>
  <div v-if="loading" class="flex justify-center py-12">
    <Loader2 class="size-8 animate-spin text-muted-foreground" />
  </div>
  <Empty v-else :class="size === 'sm' ? 'gap-3 p-4 md:p-6' : undefined">
    <EmptyHeader>
      <EmptyMedia
        v-if="slots.icon"
        variant="icon"
        :class="size === 'sm' ? 'size-10 [&_svg]:size-5' : undefined"
      >
        <slot name="icon" />
      </EmptyMedia>
      <EmptyTitle v-if="title" :class="size === 'sm' ? 'text-sm' : undefined">
        {{ title }}
      </EmptyTitle>
      <EmptyDescription
        v-if="description"
        :class="size === 'sm' ? 'text-xs' : undefined"
      >
        {{ description }}
      </EmptyDescription>
    </EmptyHeader>
    <EmptyContent v-if="slots.default">
      <slot />
    </EmptyContent>
  </Empty>
</template>
