<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" size="icon" class="size-8" title="Колонки">
        <SlidersHorizontal class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-64">
      <!-- Preset Selection -->
      <DropdownMenuLabel>Комбинированные колонки</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuRadioGroup
        :model-value="selectedPreset"
        @update:model-value="onPresetChange"
      >
        <DropdownMenuRadioItem
          v-for="preset in combinedColumnPresets"
          :key="preset.id"
          :value="preset.id"
          class="flex flex-col items-start gap-0.5 py-2"
        >
          <span class="font-medium">{{ preset.label }}</span>
          <span class="text-xs text-muted-foreground">{{
            preset.description
          }}</span>
        </DropdownMenuRadioItem>
      </DropdownMenuRadioGroup>

      <DropdownMenuSeparator />
      <DropdownMenuLabel>Поля товара</DropdownMenuLabel>
      <DropdownMenuSeparator />

      <!-- Individual columns (excluding those hidden by preset) -->
      <DropdownMenuCheckboxItem
        v-for="column in availableColumns"
        :key="column.id"
        :model-value="columnVisibility[column.id] !== false"
        :disabled="isColumnDisabledByPreset(column.id)"
        @update:model-value="(value) => toggleColumn(column.id, value)"
      >
        <span :class="{ 'text-muted-foreground': isColumnDisabledByPreset(column.id) }">
          {{ column.label }}
        </span>
      </DropdownMenuCheckboxItem>

      <!-- Store attributes section -->
      <template v-if="storeAttributes.length > 0">
        <DropdownMenuSeparator />
        <DropdownMenuLabel>Глобальные атрибуты</DropdownMenuLabel>
        <DropdownMenuSeparator />
        <DropdownMenuCheckboxItem
          v-for="attr in storeAttributes"
          :key="`storeAttr_${attr.id}`"
          :model-value="columnVisibility[`storeAttr_${attr.id}`] === true"
          @update:model-value="
            (value) => toggleColumn(`storeAttr_${attr.id}`, value)
          "
        >
          {{ attr.name }}
        </DropdownMenuCheckboxItem>
      </template>
    </DropdownMenuContent>
  </DropdownMenu>
</template>

<script setup>
// ColumnSelector - Dropdown menu for toggling column visibility and selecting combined column presets.
import { computed } from "vue";
import { SlidersHorizontal } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuCheckboxItem,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
} from "@/components/ui/dropdown-menu";
import { getHideableColumns, combinedColumnPresets } from "./columns";

const props = defineProps({
  columnVisibility: {
    type: Object,
    required: true,
  },
  storeAttributes: {
    type: Array,
    default: () => [],
  },
  selectedPreset: {
    type: String,
    default: "none",
  },
});

const emit = defineEmits(["update:columnVisibility", "update:selectedPreset"]);

const hideableColumns = getHideableColumns();

const currentPresetConfig = computed(() =>
  combinedColumnPresets.find((p) => p.id === props.selectedPreset),
);

const availableColumns = computed(() => {
  if (!currentPresetConfig.value) return hideableColumns;
  return hideableColumns.filter(
    (col) => !currentPresetConfig.value.hidesColumns.includes(col.id),
  );
});

function isColumnDisabledByPreset(columnId) {
  if (!currentPresetConfig.value) return false;
  return currentPresetConfig.value.hidesColumns.includes(columnId);
}

function onPresetChange(presetId) {
  emit("update:selectedPreset", presetId);
}

function toggleColumn(columnId, visible) {
  const newVisibility = {
    ...props.columnVisibility,
    [columnId]: visible,
  };
  emit("update:columnVisibility", newVisibility);
}
</script>
