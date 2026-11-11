<script setup>
import { Check } from "lucide-vue-next";
import { useId } from "vue";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { Input } from "@/components/ui/input";
import { FIELD_LIMITS } from "@/lib/fieldLimits";
import { t } from "@/i18n";

defineProps({
  mode: { type: String, default: "none" },
  hasExternalCategory: { type: Boolean, default: false },
  categoryName: { type: String, default: "" },
  categoryId: { type: String, default: "" },
  options: { type: Array, default: () => [] },
  disabled: { type: Boolean, default: false },
});
const emit = defineEmits([
  "update:mode",
  "update:categoryId",
  "update:categoryName",
]);
const nameInputId = `import-category-name-${useId()}`;
</script>

<template>
  <div class="grid gap-3">
    <InsetGroup>
      <InsetGroupItem
        v-for="choice in ['existing', 'new', 'none']"
        :key="choice"
        :selected="mode === choice"
        :aria-pressed="mode === choice"
        :disabled="disabled"
        @click="emit('update:mode', choice)"
      >
        <div class="flex items-center justify-between gap-3">
          <span class="min-w-0 break-words">
            {{
              choice === "existing"
                ? t("marketplace.connectionPage.dialogs.useExistingCategory")
                : choice === "new"
                  ? hasExternalCategory
                    ? t(
                        "marketplace.connectionPage.import.createNewFromAdapter",
                        { name: categoryName },
                      )
                    : t("marketplace.connectionPage.import.createNewCategory")
                  : t("marketplace.connectionPage.import.noCategory")
            }}
          </span>
          <Check
            v-if="mode === choice"
            class="size-4 shrink-0"
            aria-hidden="true"
          />
        </div>
      </InsetGroupItem>
    </InsetGroup>
    <SettingsFormField
      v-if="mode === 'existing'"
      :label="t('marketplace.connectionPage.dialogs.choosePlaceholder')"
    >
      <SearchableSelect
        :model-value="categoryId"
        :options="options"
        :disabled="disabled"
        :placeholder="t('marketplace.connectionPage.dialogs.choosePlaceholder')"
        @update:model-value="emit('update:categoryId', $event)"
      />
    </SettingsFormField>
    <SettingsFormField
      v-if="mode === 'new' && !hasExternalCategory"
      :label="t('marketplace.connectionPage.import.categoryNameLabel')"
      :label-for="nameInputId"
    >
      <Input
        :id="nameInputId"
        :model-value="categoryName"
        :disabled="disabled"
        :placeholder="
          t('marketplace.connectionPage.import.categoryNamePlaceholder')
        "
        :maxlength="FIELD_LIMITS.categoryName"
        @update:model-value="emit('update:categoryName', $event)"
      />
    </SettingsFormField>
  </div>
</template>
