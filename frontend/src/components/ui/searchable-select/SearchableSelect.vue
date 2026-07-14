<script setup>
import { computed, ref, watch, nextTick } from "vue";
import { ChevronDown, Check, Search } from "lucide-vue-next";
import {
  Popover,
  PopoverTrigger,
  PopoverContent,
} from "@/components/ui/popover";
import { cn } from "@/lib/utils";

const props = defineProps({
  options: {
    type: Array, // Array of { value: string, label: string }
    default: () => [],
  },
  modelValue: {
    type: String,
    default: "",
  },
  placeholder: {
    type: String,
    default: "Выберите...",
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

const emit = defineEmits(["update:modelValue", "open"]);

const open = ref(false);
const searchQuery = ref("");
const searchInputRef = ref(null);

// Clear search when dropdown closes
watch(open, (isOpen) => {
  if (!isOpen) {
    searchQuery.value = "";
  } else {
    emit("open");
  }
  if (isOpen && isLargeList.value) {
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
    option.label.toLowerCase().includes(query)
  );
});

// Limit displayed options for performance
const displayedOptions = computed(() => {
  const filtered = filteredOptions.value;

  // If large list and no search query, only show first N options
  if (isLargeList.value && !searchQuery.value) {
    return filtered.slice(0, props.maxDisplayOptions);
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

const selectedOption = computed(() => {
  return props.options.find(o => o.value === props.modelValue);
});

const displayText = computed(() => {
  if (selectedOption.value?.label) return selectedOption.value.label;
  if (props.modelValue) return props.modelValue;
  return props.placeholder;
});

const isSelected = (option) => {
  return props.modelValue === option.value;
};

const selectOption = (option) => {
  emit("update:modelValue", option.value);
  open.value = false;
};

const clearSelection = () => {
  emit("update:modelValue", "");
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
          !modelValue && 'text-muted-foreground',
          props.class
        )"
      >
        <span class="min-w-0 flex-1 truncate">{{ displayText }}</span>
        <ChevronDown class="size-4 shrink-0 opacity-50" />
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
            :placeholder="isLargeList ? 'Начните вводить для поиска...' : 'Поиск...'"
            class="w-full pl-8 pr-3 py-1.5 text-sm border border-input rounded-md bg-background focus:outline-none focus:ring-2 focus:ring-ring"
            @click.stop
          />
        </div>
        <!-- Info for large lists without search -->
        <div v-if="isLargeList && !searchQuery" class="mt-1.5 text-xs text-muted-foreground">
          {{ options.length.toLocaleString() }} вариантов. Используйте поиск для быстрого выбора.
        </div>
      </div>

      <!-- Options list -->
      <div class="overflow-auto flex-1">
        <div v-if="options.length === 0" class="p-3 text-sm text-muted-foreground text-center">
          Нет доступных вариантов
        </div>
        <div v-else-if="filteredOptions.length === 0" class="p-3 text-sm text-muted-foreground text-center">
          Ничего не найдено
        </div>
        <div v-else class="p-1">
          <div
            v-for="option in displayedOptions"
            :key="option.value"
            class="flex items-center gap-2 px-2 py-1.5 rounded-sm cursor-pointer hover:bg-accent hover:text-accent-foreground transition-colors"
            @click="selectOption(option)"
          >
            <Check
              :class="cn(
                'size-4 shrink-0',
                isSelected(option) ? 'opacity-100' : 'opacity-0'
              )"
            />
            <span class="text-sm flex-1 truncate" :title="option.label">{{ option.label }}</span>
          </div>

          <!-- Hint when there are more options -->
          <div v-if="hasMoreOptions" class="px-2 py-2 text-xs text-muted-foreground text-center border-t mt-1">
            <template v-if="searchQuery">
              Показано {{ displayedOptions.length }} из {{ filteredOptions.length }} результатов. Уточните запрос.
            </template>
            <template v-else>
              Ещё {{ remainingCount.toLocaleString() }} вариантов. Используйте поиск.
            </template>
          </div>
        </div>
      </div>
    </PopoverContent>
  </Popover>
  </div>
</template>
