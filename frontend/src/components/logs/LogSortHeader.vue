<script setup>
import { computed } from "vue";
import { ArrowDown, ArrowUp, ArrowUpDown } from "lucide-vue-next";
import { TableHead } from "@/components/ui/table";
import { Button } from "@/components/ui/button";

const props = defineProps({
  label: { type: String, required: true },
  column: { type: String, required: true },
  sortBy: { type: String, default: "createdAt" },
  sortDir: { type: String, default: "desc" },
  align: { type: String, default: "left" }, // left | right
  class: { type: String, default: "" },
});

const emit = defineEmits(["sort"]);

const active = computed(() => props.sortBy === props.column);
const justify = computed(() =>
  props.align === "right" ? "justify-end" : "justify-start",
);

function onClick() {
  emit("sort", props.column);
}
</script>

<template>
  <TableHead :class="props.class">
    <Button
      type="button"
      variant="ghost"
      class="h-auto min-h-0 w-full justify-start gap-1 p-0 text-left text-xs font-medium hover:bg-transparent hover:text-foreground"
      :class="justify"
      @click="onClick"
    >
      <span class="truncate">{{ label }}</span>
      <ArrowDown
        v-if="active && sortDir === 'desc'"
        class="size-3.5 shrink-0 text-foreground"
      />
      <ArrowUp
        v-else-if="active && sortDir === 'asc'"
        class="size-3.5 shrink-0 text-foreground"
      />
      <ArrowUpDown v-else class="size-3.5 shrink-0 opacity-30" />
    </Button>
  </TableHead>
</template>
