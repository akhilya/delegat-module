<script setup>
import { computed, ref, watch, nextTick } from "vue";
import { ChevronDown, Check, X, Search } from "lucide-vue-next";
import {
  Popover,
  PopoverTrigger,
  PopoverContent,
} from "@/components/ui/popover";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { cn } from "@/lib/utils";
import { t } from "@/i18n";

const props = defineProps({
  options: {
    type: Array,
    default: () => [],
  },
  modelValue: {
    type: Array,
    default: () => [],
  },
  placeholder: {
    type: String,
    default: () => t("components.multiSelectDropdown.placeholder"),
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  class: {
    type: String,
    default: "",
  },
  invalid: {
    type: Boolean,
    default: false,
  },
  searchThreshold: {
    type: Number,
    default: 5, // Show search only if more than 5 options
  },
  maxDisplayOptions: {
    type: Number,
    default: 100, // Maximum options to display at once for performance
  },
});

const emit = defineEmits(["update:modelValue"]);

const open = ref(false);
const searchQuery = ref("");
const searchInputRef = ref(null);

// Clear search when dropdown closes
watch(open, (isOpen) => {
  if (!isOpen) {
    searchQuery.value = "";
  } else if (isLargeList.value) {
    // Auto-focus search input for large lists
    nextTick(() => {
      searchInputRef.value?.focus();
    });
  }
});

// Check if this is a large list that needs special handling
const isLargeList = computed(() => props.options.length > props.maxDisplayOptions);

const showSearch = computed(() => props.options.length > props.searchThreshold);

const filteredOptions = computed(() => {
  if (!searchQuery.value) {
    return props.options;
  }
  const query = searchQuery.value.toLowerCase();
  return props.options.filter(option =>
    option.toLowerCase().includes(query)
  );
});

// Limit displayed options for performance
const displayedOptions = computed(() => {
  const filtered = filteredOptions.value;

  // If large list and no search query, show selected options first, then others
  if (isLargeList.value && !searchQuery.value) {
    // Selected options should always be visible
    const selected = filtered.filter(o => props.modelValue.includes(o));
    const unselected = filtered.filter(o => !props.modelValue.includes(o));

    // Show all selected + remaining up to limit
    const remaining = props.maxDisplayOptions - selected.length;
    return [...selected, ...unselected.slice(0, Math.max(0, remaining))];
  }

  // For filtered results, still limit but allow more
  return filtered.slice(0, props.maxDisplayOptions * 2);
});

// Show hint when there are more options than displayed
const hasMoreOptions = computed(() => {
  return filteredOptions.value.length > displayedOptions.value.length;
});

// Count of remaining options
const remainingCount = computed(() => {
  return filteredOptions.value.length - displayedOptions.value.length;
});

const selectedCount = computed(() => props.modelValue.length);

const displayText = computed(() => {
  if (selectedCount.value === 0) {
    return props.placeholder;
  }
  if (selectedCount.value === 1) {
    return props.modelValue[0];
  }
  return t("components.multiSelectDropdown.selectedCount", { count: selectedCount.value });
});

const isSelected = (option) => {
  return props.modelValue.includes(option);
};

const toggleOption = (option) => {
  const newValue = [...props.modelValue];
  const index = newValue.indexOf(option);

  if (index >= 0) {
    newValue.splice(index, 1);
  } else {
    newValue.push(option);
  }

  emit("update:modelValue", newValue);
};

const clearAll = () => {
  emit("update:modelValue", []);
};
</script>

<template>
  <div class="w-full min-w-0 max-w-full overflow-hidden">
  <Popover v-model:open="open" class="block w-full min-w-0">
    <PopoverTrigger as-child>
      <button
        type="button"
        :disabled="disabled"
        :aria-invalid="invalid || undefined"
        :class="cn(
          'border-input data-[placeholder]:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/50 flex w-full min-w-0 max-w-full items-center justify-between gap-2 overflow-hidden rounded-md border bg-transparent dark:bg-input/30 px-3 py-2 text-sm shadow-xs transition-[color,box-shadow] outline-none focus-visible:ring-[3px] disabled:cursor-not-allowed disabled:opacity-50 h-9 text-left',
          'aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive',
          selectedCount === 0 && 'text-muted-foreground',
          props.class
        )"
      >
        <span class="min-w-0 flex-1 truncate">{{ displayText }}</span>
        <div class="flex shrink-0 items-center gap-1">
          <span
            v-if="selectedCount > 0"
            class="flex items-center justify-center rounded-full bg-primary text-primary-foreground text-xs w-5 h-5"
          >
            {{ selectedCount }}
          </span>
          <X
            v-if="selectedCount > 0 && !disabled"
            class="size-4 opacity-50 hover:opacity-100 cursor-pointer"
            @click.stop="clearAll"
          />
          <ChevronDown class="size-4 shrink-0 opacity-50" />
        </div>
      </button>
    </PopoverTrigger>
    <PopoverContent
      align="start"
      :collision-padding="8"
      class="w-[min(var(--reka-popover-trigger-width),24rem)] max-w-[min(24rem,calc(100vw-2rem))] p-0 max-h-72 overflow-hidden flex flex-col"
    >
      <!-- Search input (only shown if many options) -->
      <div v-if="showSearch" class="p-2 border-b">
        <div class="relative">
          <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 size-4 text-muted-foreground" />
          <input
            ref="searchInputRef"
            v-model="searchQuery"
            type="text"
            :placeholder="isLargeList ? t('components.multiSelectDropdown.searchPlaceholderLarge') : t('components.multiSelectDropdown.searchPlaceholder')"
            class="w-full pl-8 pr-3 py-1.5 text-sm border border-input rounded-md bg-background focus:outline-none focus:ring-2 focus:ring-ring"
            @click.stop
          />
        </div>
        <!-- Info for large lists without search -->
        <div v-if="isLargeList && !searchQuery" class="mt-1.5 text-xs text-muted-foreground">
          {{ t('components.multiSelectDropdown.largeListHint', { count: options.length.toLocaleString() }) }}
        </div>
      </div>

      <!-- Options list -->
      <div class="overflow-auto flex-1">
        <div v-if="options.length === 0" class="p-3 text-sm text-muted-foreground text-center">
          {{ t('components.multiSelectDropdown.noOptions') }}
        </div>
        <div v-else-if="filteredOptions.length === 0" class="p-3 text-sm text-muted-foreground text-center">
          {{ t('components.multiSelectDropdown.nothingFound') }}
        </div>
        <div v-else class="p-1">
          <label
            v-for="option in displayedOptions"
            :key="option"
            class="flex items-center gap-2 px-2 py-1.5 rounded-sm cursor-pointer hover:bg-accent hover:text-accent-foreground transition-colors"
            @click.prevent="toggleOption(option)"
          >
            <div
              :class="cn(
                'flex h-4 w-4 shrink-0 items-center justify-center rounded-sm border border-primary',
                isSelected(option) ? 'bg-primary text-primary-foreground' : 'opacity-50'
              )"
            >
              <Check v-if="isSelected(option)" class="size-3" />
            </div>
            <span class="text-sm flex-1 truncate" :title="option">{{ option }}</span>
          </label>

          <!-- Hint when there are more options -->
          <div v-if="hasMoreOptions" class="px-2 py-2 text-xs text-muted-foreground text-center border-t mt-1">
            <template v-if="searchQuery">
              {{ t('components.multiSelectDropdown.moreResultsHint', { shown: displayedOptions.length, total: filteredOptions.length }) }}
            </template>
            <template v-else>
              {{ t('components.multiSelectDropdown.remainingOptionsHint', { count: remainingCount.toLocaleString() }) }}
            </template>
          </div>
        </div>
      </div>
    </PopoverContent>
  </Popover>
  </div>
</template>
