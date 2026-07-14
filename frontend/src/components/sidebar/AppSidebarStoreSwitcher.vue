<script setup>
import {
  SidebarMenu,
  SidebarMenuItem,
} from "@/components/ui/sidebar";
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

defineProps({
  memberships: { type: Array, default: () => [] },
  selectedStoreId: { type: [Number, String, null], default: null },
});

const emit = defineEmits(["select-store"]);
</script>

<template>
  <SidebarMenu v-if="memberships.length > 0">
    <SidebarMenuItem class="p-0">
      <Select
        :model-value="selectedStoreId != null ? String(selectedStoreId) : ''"
        @update:model-value="emit('select-store', Number($event))"
      >
        <SelectTrigger class="h-9 w-full bg-background">
          <SelectValue placeholder="Магазин" />
        </SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectLabel>Магазины</SelectLabel>
            <SelectItem
              v-for="store in memberships"
              :key="store.id"
              :value="String(store.id)"
            >
              {{ store.name }}
            </SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
    </SidebarMenuItem>
  </SidebarMenu>
</template>