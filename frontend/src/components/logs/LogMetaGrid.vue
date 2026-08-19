<script setup>
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";

defineProps({
  items: {
    type: Array,
    default: () => [],
  },
});
</script>

<template>
  <InsetGroup>
    <InsetGroupItem
      v-for="(item, i) in items"
      :key="i"
      as="div"
      class="items-start"
    >
      <span class="text-muted-foreground">{{ item.label }}</span>
      <template #trailing>
        <slot :name="`item-${i}`" :item="item">
          <span
            class="block max-w-[16rem] text-right break-all"
            :title="item.value == null || item.value === '' ? undefined : String(item.value)"
          >
            {{ item.value ?? "-" }}
          </span>
        </slot>
      </template>
    </InsetGroupItem>
  </InsetGroup>
</template>
