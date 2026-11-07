<script setup>
import { useVModel } from "@vueuse/core";
import { SlidersHorizontal } from "lucide-vue-next";
import { useI18n } from "vue-i18n";
import NavSearchField from "@/components/NavSearchField.vue";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";

const props = defineProps({
  modelValue: { type: String, default: "" },
  placeholder: { type: String, default: "" },
  activeFilterCount: { type: Number, default: 0 },
  filtersOpen: { type: Boolean, default: false },
});
const emit = defineEmits([
  "update:modelValue",
  "update:filtersOpen",
  "submit",
  "reset",
]);
const open = useVModel(props, "filtersOpen", emit, { passive: true });
const { t } = useI18n();
</script>

<template>
  <div
    data-slot="page-list-toolbar"
    class="flex w-full min-w-0 max-w-md items-center gap-2"
  >
    <NavSearchField
      :model-value="modelValue"
      :placeholder="placeholder"
      class="flex-1"
      @update:model-value="emit('update:modelValue', $event)"
      @submit="emit('submit')"
    />
    <DropdownMenu v-if="$slots.filters" v-model:open="open">
      <DropdownMenuTrigger as-child>
        <Button
          variant="outline"
          size="icon"
          class="shrink-0"
          :aria-label="
            activeFilterCount
              ? t('common.activeFilters', { count: activeFilterCount })
              : t('common.filters')
          "
          :title="t('common.filters')"
          :data-active="activeFilterCount > 0 || undefined"
        >
          <SlidersHorizontal class="size-4" aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="end"
        :collision-padding="12"
        class="w-56"
        :aria-label="t('common.filters')"
      >
        <slot name="filters" />
        <template v-if="activeFilterCount > 0">
          <DropdownMenuSeparator />
          <DropdownMenuItem @select="emit('reset')">{{
            t("common.reset")
          }}</DropdownMenuItem>
        </template>
      </DropdownMenuContent>
    </DropdownMenu>
    <div v-if="$slots.actions" class="flex shrink-0 items-center gap-2">
      <slot name="actions" />
    </div>
  </div>
</template>
