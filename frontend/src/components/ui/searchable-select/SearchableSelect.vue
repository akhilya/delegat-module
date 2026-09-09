<script setup>
import { computed, ref, watch, nextTick } from "vue";
import { ChevronDown, Check, Search, Plus } from "lucide-vue-next";
import {
  Popover,
  PopoverTrigger,
  PopoverContent,
} from "@/components/ui/popover";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { MENU_ITEM, MENU_ITEM_CHECK } from "@/lib/menuSurface";
import { t } from "@/i18n";

function focusSearchInput() {
  const el = searchInputRef.value?.$el ?? searchInputRef.value;
  el?.focus?.();
}

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
    default: () => t("components.searchableSelect.placeholder"),
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
  /** When true, search is driven by the parent (emit `search`); local filter is skipped. */
  remote: {
    type: Boolean,
    default: false,
  },
  loading: {
    type: Boolean,
    default: false,
  },
  /** Allow typing a value that is not in `options`. */
  allowCreate: {
    type: Boolean,
    default: false,
  },
  contentClass: {
    type: String,
    default: "",
  },
});

const emit = defineEmits(["update:modelValue", "open", "search"]);

const open = ref(false);
const searchQuery = ref("");
const searchInputRef = ref(null);

// Clear search when dropdown closes
watch(open, (isOpen) => {
  if (!isOpen) {
    searchQuery.value = "";
  } else {
    emit("open");
    if (props.remote) emit("search", "");
  }
  if (isOpen && (isLargeList.value || props.remote || props.allowCreate)) {
    nextTick(() => {
      focusSearchInput();
    });
  }
});

watch(searchQuery, (query) => {
  if (props.remote && open.value) emit("search", query);
});

// Check if this is a large list that needs special handling
const isLargeList = computed(
  () => props.options.length > props.maxDisplayOptions,
);

const showSearch = computed(
  () =>
    props.allowCreate ||
    props.remote ||
    props.options.length > props.searchThreshold,
);

const filteredOptions = computed(() => {
  if (props.remote || !searchQuery.value) {
    return props.options;
  }
  const query = searchQuery.value.toLowerCase();
  return props.options.filter((option) =>
    option.label.toLowerCase().includes(query),
  );
});

// Limit displayed options for performance
const displayedOptions = computed(() => {
  const filtered = filteredOptions.value;

  // If large list and no search query, only show first N options
  if (!props.remote && isLargeList.value && !searchQuery.value) {
    return filtered.slice(0, props.maxDisplayOptions);
  }

  // For filtered results, still limit but allow more
  return filtered.slice(0, props.maxDisplayOptions * 2);
});

// Show hint when there are more options than displayed
const hasMoreOptions = computed(() => {
  if (props.remote) return false;
  return filteredOptions.value.length > displayedOptions.value.length;
});

// Count of remaining options
const remainingCount = computed(() => {
  return filteredOptions.value.length - displayedOptions.value.length;
});

const trimmedQuery = computed(() => searchQuery.value.trim());

const normalizedModelValue = computed(() =>
  props.modelValue == null || props.modelValue === ""
    ? ""
    : String(props.modelValue),
);

const canAddCustom = computed(() => {
  if (!props.allowCreate) return false;
  const q = trimmedQuery.value;
  if (!q) return false;
  const lower = q.toLowerCase();
  if (normalizedModelValue.value.toLowerCase() === lower) return false;
  return !props.options.some(
    (option) =>
      String(option.value).toLowerCase() === lower ||
      String(option.label).toLowerCase() === lower,
  );
});

const extraSelectedOption = computed(() => {
  const value = normalizedModelValue.value;
  if (!value) return null;
  const exists = props.options.some(
    (option) =>
      String(option.value) === value || String(option.label) === value,
  );
  if (exists) return null;
  return { value, label: value };
});

const addCustom = () => {
  if (!canAddCustom.value) return;
  emit("update:modelValue", trimmedQuery.value);
  open.value = false;
};

const selectedOption = computed(() => {
  return props.options.find(
    (o) => String(o.value) === normalizedModelValue.value,
  );
});

const displayText = computed(() => {
  if (selectedOption.value?.label) return selectedOption.value.label;
  if (props.modelValue) return props.modelValue;
  return props.placeholder;
});

const isSelected = (option) => {
  return String(option.value) === normalizedModelValue.value;
};

const selectOption = (option) => {
  emit("update:modelValue", option.value);
  open.value = false;
};

const clearSelection = () => {
  emit("update:modelValue", "");
};

const closePopover = () => {
  open.value = false;
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
          :class="
            cn(
              'border-input data-[placeholder]:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/50 flex w-full min-w-0 max-w-full items-center justify-between gap-2 overflow-hidden rounded-md border bg-transparent dark:bg-input/30 px-3 py-2 text-sm shadow-xs transition-[color,box-shadow] outline-none focus-visible:ring-[3px] disabled:cursor-not-allowed disabled:opacity-50 h-9 text-left',
              'aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive',
              !modelValue && 'text-muted-foreground',
              props.class,
            )
          "
        >
          <span class="min-w-0 flex-1 truncate">{{ displayText }}</span>
          <ChevronDown class="size-4 shrink-0 opacity-50" />
        </button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        :collision-padding="8"
        :class="
          cn(
            'w-[min(var(--reka-popover-trigger-width),24rem)] max-w-[min(24rem,calc(100vw-2rem))] p-0 max-h-72 overflow-hidden flex flex-col',
            props.contentClass,
          )
        "
      >
        <!-- Search input (only shown if many options / remote) -->
        <div v-if="showSearch" class="p-2 border-b">
          <div class="relative">
            <Search
              class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            />
            <Input
              ref="searchInputRef"
              v-model="searchQuery"
              type="search"
              :placeholder="
                remote || isLargeList
                  ? t('components.searchableSelect.searchPlaceholderLarge')
                  : t('components.searchableSelect.searchPlaceholder')
              "
              class="h-8 pl-8"
              @click.stop
              @keydown.enter.prevent.stop="addCustom"
            />
          </div>
          <div
            v-if="remote && loading"
            class="mt-1.5 text-xs text-muted-foreground"
          >
            {{ t("components.searchableSelect.searching") }}
          </div>
          <div
            v-else-if="isLargeList && !searchQuery && !remote"
            class="mt-1.5 text-xs text-muted-foreground"
          >
            {{
              t("components.searchableSelect.largeListHint", {
                count: options.length.toLocaleString(),
              })
            }}
          </div>
        </div>

        <!-- Options list -->
        <div class="overflow-auto flex-1">
          <div
            v-if="loading && options.length === 0"
            class="p-3 text-sm text-muted-foreground text-center"
          >
            {{ t("components.searchableSelect.searching") }}
          </div>
          <div
            v-else-if="options.length === 0 && !canAddCustom && !extraSelectedOption"
            class="p-3 text-sm text-muted-foreground text-center"
          >
            {{
              allowCreate
                ? t("components.searchableSelect.typeToAdd")
                : remote && searchQuery
                  ? t("components.searchableSelect.nothingFound")
                  : t("components.searchableSelect.noOptions")
            }}
          </div>
          <div
            v-else-if="filteredOptions.length === 0 && !canAddCustom && !extraSelectedOption"
            class="p-3 text-sm text-muted-foreground text-center"
          >
            {{ t("components.searchableSelect.nothingFound") }}
          </div>
          <div v-else class="p-1">
            <button
              v-if="canAddCustom"
              type="button"
              class="w-full text-left"
              :class="MENU_ITEM"
              @click="addCustom"
            >
              <Plus class="size-4 shrink-0" />
              <span class="text-sm flex-1 truncate">{{
                t("components.searchableSelect.addCustom", {
                  value: trimmedQuery,
                })
              }}</span>
            </button>
            <div
              v-if="extraSelectedOption"
              :class="cn(MENU_ITEM, 'pr-8')"
              @click="selectOption(extraSelectedOption)"
            >
              <span class="text-sm flex-1 truncate" :title="extraSelectedOption.label">{{
                extraSelectedOption.label
              }}</span>
              <Check :class="cn(MENU_ITEM_CHECK, 'text-foreground')" />
            </div>
            <div
              v-for="option in displayedOptions"
              :key="option.value"
              :class="cn(MENU_ITEM, 'pr-8')"
              @click="selectOption(option)"
            >
              <span class="text-sm flex-1 truncate" :title="option.label">{{
                option.label
              }}</span>
              <Check
                v-if="isSelected(option)"
                :class="cn(MENU_ITEM_CHECK, 'text-foreground')"
              />
            </div>

            <!-- Hint when there are more options -->
            <div
              v-if="hasMoreOptions"
              class="px-2 py-2 text-xs text-muted-foreground text-center border-t mt-1"
            >
              <template v-if="searchQuery">
                {{
                  t("components.searchableSelect.moreResultsHint", {
                    shown: displayedOptions.length,
                    total: filteredOptions.length,
                  })
                }}
              </template>
              <template v-else>
                {{
                  t("components.searchableSelect.remainingOptionsHint", {
                    count: remainingCount.toLocaleString(),
                  })
                }}
              </template>
            </div>
          </div>
        </div>
        <div v-if="$slots.footer" class="shrink-0 border-t p-1">
          <slot name="footer" :close="closePopover" />
        </div>
      </PopoverContent>
    </Popover>
  </div>
</template>
