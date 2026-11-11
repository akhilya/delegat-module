<script setup>
import { computed, ref } from "vue";
import {
  Combobox,
  ComboboxAnchor,
  ComboboxEmpty,
  ComboboxGroup,
  ComboboxInput,
  ComboboxItem,
  ComboboxItemIndicator,
  ComboboxList,
  ComboboxTrigger,
  ComboboxViewport,
} from "@/components/ui/combobox";
import { Button } from "@/components/ui/button";
import { Check, ChevronsUpDown, Plus, X } from "@/lib/icons";
import { ATTRIBUTE_UNITS, getAttributeUnit } from "@/lib/attributeUnits";
import { useI18n } from "vue-i18n";

const { t } = useI18n();

const props = defineProps({
  id: { type: String, required: true },
  modelValue: { type: String, default: "" },
  placeholder: { type: String, required: true },
  disabled: { type: Boolean, default: false },
});

const emit = defineEmits(["update:modelValue"]);

const isOpen = ref(false);
const searchTerm = ref("");

const unitOptions = computed(() =>
  ATTRIBUTE_UNITS.map((unit) => {
    const name = t(`components.attributeUnitSelect.units.${unit.code}`);

    return {
      ...unit,
      name,
      textValue: `${name} ${unit.code} ${unit.symbol}`,
    };
  }),
);

const currentValue = computed(() => String(props.modelValue ?? "").trim());
const selectedUnit = computed(() => getAttributeUnit(currentValue.value));
const selectedValue = computed(
  () => selectedUnit.value?.code || currentValue.value,
);
const selectedOption = computed(() =>
  unitOptions.value.find((unit) => unit.code === selectedUnit.value?.code),
);
const unknownValue = computed(() =>
  currentValue.value && !selectedUnit.value ? currentValue.value : "",
);
const hasValue = computed(() => currentValue.value.length > 0);

const customValue = computed(() => {
  const query = searchTerm.value.trim();
  if (!query || query.toLowerCase() === currentValue.value.toLowerCase()) {
    return "";
  }

  const queryLower = query.toLowerCase();
  const isKnownOption = unitOptions.value.some(
    (unit) =>
      unit.code.toLowerCase() === queryLower ||
      unit.symbol.toLowerCase() === queryLower ||
      unit.name.toLowerCase() === queryLower,
  );

  return isKnownOption ? "" : query;
});

const displaySearchValue = () => "";

function updateValue(value) {
  emit("update:modelValue", value == null ? "" : String(value));
}

function clearValue() {
  searchTerm.value = "";
  emit("update:modelValue", "");
}
</script>

<template>
  <div class="flex min-w-0 items-center gap-1.5">
    <Combobox
      class="min-w-0 flex-1"
      :model-value="selectedValue"
      :open="isOpen"
      :disabled="disabled"
      @update:model-value="updateValue"
      @update:open="isOpen = $event"
    >
      <ComboboxAnchor as-child class="w-full">
        <ComboboxTrigger as-child>
          <Button
            :id="id"
            type="button"
            variant="outline"
            class="w-full min-w-0 justify-between font-normal"
            :disabled="disabled"
          >
            <span
              v-if="selectedOption"
              class="flex min-w-0 flex-1 items-center gap-2"
            >
              <span class="min-w-0 flex-1 truncate text-left">
                {{ selectedOption.name }}
              </span>
              <span class="flex shrink-0 items-center gap-1">
                <span
                  class="rounded-sm bg-muted px-1.5 py-0.5 font-mono text-xs text-foreground"
                >
                  {{ selectedOption.symbol }}
                </span>
                <span
                  v-if="selectedOption.code !== selectedOption.symbol"
                  class="rounded-sm bg-muted/60 px-1.5 py-0.5 font-mono text-xs text-muted-foreground"
                >
                  {{ selectedOption.code }}
                </span>
              </span>
            </span>
            <span
              v-else
              class="min-w-0 flex-1 truncate text-left"
              :class="!hasValue && 'text-muted-foreground'"
            >
              {{ currentValue || placeholder }}
            </span>
            <ChevronsUpDown class="size-4 shrink-0 text-muted-foreground" />
          </Button>
        </ComboboxTrigger>
      </ComboboxAnchor>

      <ComboboxList
        align="start"
        :collision-padding="8"
        class="w-[min(24rem,calc(100vw-2rem))] p-0"
      >
        <ComboboxInput
          v-model="searchTerm"
          :display-value="displaySearchValue"
          :placeholder="t('components.attributeUnitSelect.searchPlaceholder')"
          :maxlength="64"
        />
        <ComboboxViewport class="max-h-72 overflow-y-auto p-1">
          <ComboboxEmpty class="px-3 py-2 text-left text-muted-foreground">
            {{ t("components.searchableSelect.nothingFound") }}
          </ComboboxEmpty>
          <ComboboxGroup>
            <ComboboxItem
              v-if="customValue"
              :value="customValue"
              :text-value="customValue"
            >
              <Plus class="size-4 shrink-0" />
              <span class="min-w-0 flex-1 truncate">
                {{
                  t("components.searchableSelect.addCustom", {
                    value: customValue,
                  })
                }}
              </span>
              <ComboboxItemIndicator>
                <Check class="size-4 text-foreground" />
              </ComboboxItemIndicator>
            </ComboboxItem>

            <ComboboxItem
              v-if="unknownValue"
              :value="unknownValue"
              :text-value="unknownValue"
            >
              <span class="min-w-0 flex-1 truncate" :title="unknownValue">
                {{ unknownValue }}
              </span>
              <ComboboxItemIndicator>
                <Check class="size-4 text-foreground" />
              </ComboboxItemIndicator>
            </ComboboxItem>

            <ComboboxItem
              v-for="unit in unitOptions"
              :key="unit.code"
              :value="unit.code"
              :text-value="unit.textValue"
            >
              <span class="min-w-0 flex-1 truncate" :title="unit.textValue">
                {{ unit.name }}
              </span>
              <span
                class="shrink-0 rounded-sm bg-muted px-1.5 py-0.5 font-mono text-xs text-foreground"
              >
                {{ unit.symbol }}
              </span>
              <span
                v-if="unit.code !== unit.symbol"
                class="shrink-0 rounded-sm bg-muted/60 px-1.5 py-0.5 font-mono text-xs text-muted-foreground"
              >
                {{ unit.code }}
              </span>
              <ComboboxItemIndicator>
                <Check class="size-4 text-foreground" />
              </ComboboxItemIndicator>
            </ComboboxItem>
          </ComboboxGroup>
        </ComboboxViewport>
      </ComboboxList>
    </Combobox>

    <Button
      v-if="hasValue"
      type="button"
      variant="ghost"
      size="icon"
      class="shrink-0 text-muted-foreground"
      :disabled="disabled"
      :aria-label="t('components.attributeUnitSelect.clearLabel')"
      :title="t('components.attributeUnitSelect.clearLabel')"
      @click="clearValue"
    >
      <X class="size-4" />
    </Button>
  </div>
</template>
