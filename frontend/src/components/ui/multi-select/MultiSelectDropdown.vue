<script setup>
import {
  computed,
  ref,
  watch,
  nextTick,
  onMounted,
  onBeforeUnmount,
} from "vue";
import { ChevronDown, X, Search, Plus } from "lucide-vue-next";
import {
  Popover,
  PopoverTrigger,
  PopoverContent,
} from "@/components/ui/popover";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import { MENU_ITEM } from "@/lib/menuSurface";
import { t } from "@/i18n";

const PILL_CLASS = "h-5 max-w-none px-1.5 font-medium";

function focusSearchInput() {
  const el = searchInputRef.value?.$el ?? searchInputRef.value;
  el?.focus?.();
}

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
  /** Allow typing a value that is not in `options`. */
  allowCreate: {
    type: Boolean,
    default: false,
  },
  /** Max selected values. 0 / omitted = unlimited. */
  maxSelections: {
    type: Number,
    default: 0,
  },
});

const emit = defineEmits(["update:modelValue", "open"]);

const open = ref(false);
const searchQuery = ref("");
const searchInputRef = ref(null);

// Clear search when dropdown closes
watch(open, (isOpen) => {
  if (isOpen) emit("open");
  if (!isOpen) {
    searchQuery.value = "";
  } else if (isLargeList.value || props.allowCreate) {
    nextTick(() => {
      focusSearchInput();
    });
  }
});

// Check if this is a large list that needs special handling
const isLargeList = computed(
  () => props.options.length > props.maxDisplayOptions,
);

const showSearch = computed(
  () => props.allowCreate || props.options.length > props.searchThreshold,
);

const filteredOptions = computed(() => {
  if (!searchQuery.value) {
    return props.options;
  }
  const query = searchQuery.value.toLowerCase();
  return props.options.filter((option) => option.toLowerCase().includes(query));
});

// Limit displayed options for performance
const displayedOptions = computed(() => {
  const filtered = filteredOptions.value;

  // If large list and no search query, show selected options first, then others
  if (isLargeList.value && !searchQuery.value) {
    // Selected options should always be visible
    const selected = filtered.filter((o) => props.modelValue.includes(o));
    const unselected = filtered.filter((o) => !props.modelValue.includes(o));

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

const trimmedQuery = computed(() => searchQuery.value.trim());

const selectedCount = computed(() => props.modelValue.length);

const maxCap = computed(() => {
  const n = Number(props.maxSelections);
  return Number.isInteger(n) && n > 0 ? n : null;
});

const atLimit = computed(
  () => maxCap.value != null && selectedCount.value >= maxCap.value,
);

const canAddCustom = computed(() => {
  if (!props.allowCreate || atLimit.value) return false;
  const q = trimmedQuery.value;
  if (!q) return false;
  const lower = q.toLowerCase();
  if (props.modelValue.some((value) => String(value).toLowerCase() === lower)) {
    return false;
  }
  return !props.options.some(
    (option) => String(option).toLowerCase() === lower,
  );
});

const extraSelected = computed(() =>
  props.modelValue.filter((value) => !props.options.includes(value)),
);

const addCustom = () => {
  if (!canAddCustom.value) return;
  emit("update:modelValue", [...props.modelValue, trimmedQuery.value]);
  searchQuery.value = "";
};

const displayText = computed(() => {
  if (selectedCount.value === 0) {
    return props.placeholder;
  }
  return t(
    "components.multiSelectDropdown.selectedCount",
    selectedCount.value,
    { count: selectedCount.value },
  );
});

const viewportRef = ref(null);
const measureRef = ref(null);
const pillsFit = ref(false);
let pillsObserver = null;

function measurePills() {
  const viewport = viewportRef.value;
  const measure = measureRef.value;
  if (!viewport || !measure || props.modelValue.length === 0) {
    pillsFit.value = false;
    return;
  }
  pillsFit.value = measure.scrollWidth <= viewport.clientWidth + 0.5;
}

function scheduleMeasure() {
  nextTick(() => {
    requestAnimationFrame(measurePills);
  });
}

watch(() => props.modelValue, scheduleMeasure, { deep: true });

onMounted(() => {
  pillsObserver = new ResizeObserver(scheduleMeasure);
  if (viewportRef.value) pillsObserver.observe(viewportRef.value);
  scheduleMeasure();
});

onBeforeUnmount(() => {
  pillsObserver?.disconnect();
  pillsObserver = null;
});

const isSelected = (option) => {
  return props.modelValue.includes(option);
};

const toggleOption = (option) => {
  const newValue = [...props.modelValue];
  const index = newValue.indexOf(option);

  if (index >= 0) {
    newValue.splice(index, 1);
  } else if (atLimit.value) {
    return;
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
          :class="
            cn(
              'border-input data-[placeholder]:text-muted-foreground focus-visible:border-ring focus-visible:ring-ring/50 flex w-full min-w-0 max-w-full items-center justify-between gap-2 overflow-hidden rounded-control border bg-transparent dark:bg-input/30 px-3 py-2 text-sm shadow-xs transition-[color,box-shadow] outline-none focus-visible:ring-[3px] disabled:cursor-not-allowed disabled:opacity-50 h-9 text-left',
              'aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 aria-invalid:border-destructive',
              selectedCount === 0 && 'text-muted-foreground',
              props.class,
            )
          "
          :title="selectedCount > 0 ? modelValue.join(', ') : undefined"
        >
          <span
            ref="viewportRef"
            class="relative min-h-5 min-w-0 flex-1 overflow-hidden"
          >
            <span
              ref="measureRef"
              class="invisible pointer-events-none absolute inset-y-0 left-0 flex w-max items-center gap-1 whitespace-nowrap"
              aria-hidden="true"
            >
              <Badge
                v-for="(value, index) in modelValue"
                :key="'measure-' + index + '-' + value"
                as="span"
                variant="secondary"
                :class="PILL_CLASS"
                >{{ value }}</Badge
              >
            </span>
            <span
              v-if="selectedCount > 0 && pillsFit"
              class="flex h-5 items-center gap-1"
            >
              <Badge
                v-for="(value, index) in modelValue"
                :key="'pill-' + index + '-' + value"
                as="span"
                variant="secondary"
                :class="PILL_CLASS"
                >{{ value }}</Badge
              >
            </span>
            <span v-else class="block truncate">{{ displayText }}</span>
          </span>
          <div class="flex shrink-0 items-center gap-1">
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
            <Search
              class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            />
            <Input
              ref="searchInputRef"
              v-model="searchQuery"
              type="search"
              :placeholder="
                isLargeList
                  ? t('components.multiSelectDropdown.searchPlaceholderLarge')
                  : t('components.multiSelectDropdown.searchPlaceholder')
              "
              class="h-8 pl-8"
              @click.stop
              @keydown.enter.prevent.stop="addCustom"
            />
          </div>
          <!-- Info for large lists without search -->
          <div
            v-if="isLargeList && !searchQuery"
            class="mt-1.5 text-xs text-muted-foreground"
          >
            {{
              t("components.multiSelectDropdown.largeListHint", {
                count: options.length.toLocaleString(),
              })
            }}
          </div>
        </div>

        <!-- Options list -->
        <div class="overflow-auto flex-1">
          <div
            v-if="
              options.length === 0 &&
              extraSelected.length === 0 &&
              !canAddCustom
            "
            class="p-3 text-sm text-muted-foreground text-center"
          >
            {{
              allowCreate
                ? t("components.multiSelectDropdown.typeToAdd")
                : t("components.multiSelectDropdown.noOptions")
            }}
          </div>
          <div
            v-else-if="
              filteredOptions.length === 0 &&
              extraSelected.length === 0 &&
              !canAddCustom
            "
            class="p-3 text-sm text-muted-foreground text-center"
          >
            {{ t("components.multiSelectDropdown.nothingFound") }}
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
                t("components.multiSelectDropdown.addCustom", {
                  value: trimmedQuery,
                })
              }}</span>
            </button>
            <div
              v-for="option in extraSelected"
              :key="`custom-${option}`"
              role="option"
              :aria-selected="true"
              :class="MENU_ITEM"
              @click="toggleOption(option)"
            >
              <Checkbox
                :model-value="true"
                class="pointer-events-none"
                tabindex="-1"
              />
              <span class="text-sm flex-1 truncate" :title="option">{{
                option
              }}</span>
            </div>
            <div
              v-for="option in displayedOptions"
              :key="option"
              role="option"
              :aria-selected="isSelected(option)"
              :aria-disabled="(atLimit && !isSelected(option)) || undefined"
              :class="[
                MENU_ITEM,
                atLimit && !isSelected(option)
                  ? 'opacity-50 cursor-not-allowed hover:bg-transparent'
                  : '',
              ]"
              @click="toggleOption(option)"
            >
              <Checkbox
                :model-value="isSelected(option)"
                class="pointer-events-none"
                tabindex="-1"
              />
              <span class="text-sm flex-1 truncate" :title="option">{{
                option
              }}</span>
            </div>

            <!-- Hint when there are more options -->
            <div
              v-if="hasMoreOptions"
              class="px-2 py-2 text-xs text-muted-foreground text-center border-t mt-1"
            >
              <template v-if="searchQuery">
                {{
                  t("components.multiSelectDropdown.moreResultsHint", {
                    shown: displayedOptions.length,
                    total: filteredOptions.length,
                  })
                }}
              </template>
              <template v-else>
                {{
                  t("components.multiSelectDropdown.remainingOptionsHint", {
                    count: remainingCount.toLocaleString(),
                  })
                }}
              </template>
            </div>
            <div
              v-if="maxCap"
              class="px-2 py-1.5 text-xs text-muted-foreground text-center border-t"
            >
              {{
                t("components.multiSelectDropdown.maxSelections", {
                  count: selectedCount,
                  max: maxCap,
                })
              }}
            </div>
          </div>
        </div>
      </PopoverContent>
    </Popover>
  </div>
</template>
