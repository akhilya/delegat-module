<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import Switch from "@/components/ui/switch/Switch.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Save, Trash2, Download, Upload, Settings2 } from "lucide-vue-next";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_STORE_ATTRIBUTES, setPageTitle } from "@/router";
import {
  LARGE_OPTIONS_THRESHOLD,
  STORE_ATTRIBUTE_TYPES,
  STORE_ATTRIBUTE_TYPE_VALUES,
  countAttributeOptions,
  emptyStoreAttributeForm,
  formatAttributeOptionsList,
  formFromStoreAttribute,
  isNumberAttributeType,
  isOptionsAttributeType,
  isTextAttributeType,
  parseAttributeOptions,
  resolveStoreAttributeTypeValue,
  storeAttributeSavePayload,
} from "@/lib/storeAttributeUi";

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageSection,
    Button,
    Input,
    Textarea,
    Switch,
    SettingsFormField,
    SettingsFormSection,
    InsetGroup,
    InsetGroupItem,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Save,
    Trash2,
    Download,
    Upload,
    Settings2,
    SearchableSelect,
    EmptyState,
  },
  data() {
    return {
      attribute: null,
      isLoading: true,
      isSaving: false,
      FIELD_LIMITS,
      attributeForm: emptyStoreAttributeForm(),
      attributeOptionsJson: null,
      cachedOptionsCount: 0,
      defaultValueOptions: [],
      defaultValueOptionsReady: false,
      isLoadingDefaultValueOptions: false,
      STORE_ATTRIBUTE_TYPE_VALUES,
      PAGE_STORE_ATTRIBUTES,
    };
  },
  computed: {
    attributeId() {
      return parseInt(this.$route.params.id, 10);
    },
    typeValue() {
      return resolveStoreAttributeTypeValue(this.attributeForm.type);
    },
    attributeTypeLabel() {
      return this.getAttributeTypeLabel(this.typeValue);
    },
    showOptionsField() {
      return isOptionsAttributeType(this.typeValue);
    },
    isTextType() {
      return isTextAttributeType(this.typeValue);
    },
    isNumberType() {
      return isNumberAttributeType(this.typeValue);
    },
    hasLargeOptions() {
      return this.cachedOptionsCount > LARGE_OPTIONS_THRESHOLD;
    },
    editingOptionsCount() {
      return this.cachedOptionsCount;
    },
    defaultValueSelectOptions() {
      if (!this.defaultValueOptionsReady) return [];
      return this.defaultValueOptions;
    },
  },
  watch: {
    attributeId: {
      immediate: true,
      handler() {
        this.fetchAttribute();
      },
    },
  },
  methods: {
    ...mapActions(["getStoreAttribute", "updateStoreAttribute"]),

    getAttributeTypeLabel(type) {
      const i18nKey =
        STORE_ATTRIBUTE_TYPES[type]?.i18nKey ||
        STORE_ATTRIBUTE_TYPES[resolveStoreAttributeTypeValue(type)]?.i18nKey;
      return i18nKey
        ? this.$t(`pages.storeAttributesPage.types.${i18nKey}`)
        : this.$t("pages.storeAttributesPage.types.unknown");
    },

    applyLoadedAttribute(attribute) {
      const loaded = formFromStoreAttribute(attribute);
      this.attribute = attribute;
      this.attributeForm = loaded.form;
      this.attributeOptionsJson = loaded.optionsJson;
      this.cachedOptionsCount = loaded.optionsCount;
      this.primeDefaultValueOptions();
      setPageTitle(attribute.name);
    },

    async fetchAttribute() {
      if (!Number.isInteger(this.attributeId) || this.attributeId <= 0) {
        this.attribute = null;
        this.isLoading = false;
        return;
      }

      this.isLoading = true;
      this.attribute = null;
      await this.getStoreAttribute({
        id: this.attributeId,
        onSuccess: (response) => {
          this.applyLoadedAttribute(response.data.value);
        },
        onError: (error) => {
          this.attribute = null;
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    setAttributeRequired(value) {
      this.attributeForm.isRequired = !!value;
      if (this.attributeForm.isRequired) {
        this.attributeForm.showOnProductPage = true;
      }
    },

    setShowOnProductPage(value) {
      if (this.attributeForm.isRequired) {
        this.attributeForm.showOnProductPage = true;
        return;
      }
      this.attributeForm.showOnProductPage = !!value;
    },

    resetOptionsState() {
      this.cachedOptionsCount = 0;
      this.defaultValueOptions = [];
      this.defaultValueOptionsReady = false;
      this.isLoadingDefaultValueOptions = false;
    },

    getOptionsRaw() {
      if (this.attributeOptionsJson) return this.attributeOptionsJson;
      if (this.attributeForm.options?.trim()) return this.attributeForm.options;
      return this.attribute?.options || "";
    },

    syncOptionsCountCache() {
      const raw = this.getOptionsRaw();
      this.cachedOptionsCount = raw ? countAttributeOptions(raw) : 0;
    },

    primeDefaultValueOptions() {
      const raw = this.getOptionsRaw();
      if (!raw) {
        this.defaultValueOptions = [];
        this.defaultValueOptionsReady = true;
        return;
      }

      if (this.cachedOptionsCount <= LARGE_OPTIONS_THRESHOLD) {
        this.defaultValueOptions = parseAttributeOptions(raw);
        this.defaultValueOptionsReady = true;
        return;
      }

      this.defaultValueOptions = [];
      this.defaultValueOptionsReady = false;
    },

    loadDefaultValueOptionsIfNeeded() {
      if (this.defaultValueOptionsReady || this.isLoadingDefaultValueOptions) {
        return;
      }

      const raw = this.getOptionsRaw();
      if (!raw) {
        this.defaultValueOptions = [];
        this.defaultValueOptionsReady = true;
        return;
      }

      this.isLoadingDefaultValueOptions = true;
      window.setTimeout(() => {
        this.defaultValueOptions = parseAttributeOptions(raw);
        this.defaultValueOptionsReady = true;
        this.isLoadingDefaultValueOptions = false;
      }, 0);
    },

    applyImportedOptions(parsed) {
      if (!parsed.length) {
        displaySonnerError(
          this.$t("pages.storeAttributesPage.options.fileHasNoOptions"),
        );
        return;
      }

      const json = JSON.stringify(parsed);
      if (parsed.length > LARGE_OPTIONS_THRESHOLD) {
        this.attributeOptionsJson = json;
        this.attributeForm.options = "";
      } else {
        this.attributeOptionsJson = null;
        this.attributeForm.options = formatAttributeOptionsList(parsed);
      }

      if (this.attribute) {
        this.attribute = { ...this.attribute, options: json };
      }

      this.cachedOptionsCount = parsed.length;
      this.primeDefaultValueOptions();
      displaySonnerSuccess(
        this.$t("pages.storeAttributesPage.options.imported", {
          count: parsed.length.toLocaleString(),
        }),
      );
    },

    exportAttributeOptions() {
      const raw = this.getOptionsRaw();
      const parsed = parseAttributeOptions(raw);
      if (!parsed.length) {
        displaySonnerError(
          this.$t("pages.storeAttributesPage.options.noOptionsToExport"),
        );
        return;
      }

      const json = JSON.stringify(parsed, null, 2);
      const blob = new Blob([json], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      const key = this.attributeForm.key?.trim() || "attribute";
      anchor.href = url;
      anchor.download = `${key}-options.json`;
      anchor.click();
      URL.revokeObjectURL(url);
      displaySonnerSuccess(
        this.$t("pages.storeAttributesPage.options.exported", {
          count: parsed.length.toLocaleString(),
        }),
      );
    },

    triggerOptionsImport() {
      this.$refs.optionsFileInput?.click();
    },

    handleOptionsImport(event) {
      const file = event.target.files?.[0];
      if (!file) return;

      const reader = new FileReader();
      reader.onload = (loadEvent) => {
        try {
          const text = loadEvent.target?.result;
          if (typeof text !== "string") {
            displaySonnerError(
              this.$t("pages.storeAttributesPage.options.readFailed"),
            );
            return;
          }
          this.applyImportedOptions(parseAttributeOptions(text));
        } catch {
          displaySonnerError(
            this.$t("pages.storeAttributesPage.options.readError"),
          );
        }
      };
      reader.onerror = () => {
        displaySonnerError(
          this.$t("pages.storeAttributesPage.options.readError"),
        );
      };
      reader.readAsText(file);
      event.target.value = "";
    },

    clearAttributeOptions() {
      this.attributeOptionsJson = null;
      this.attributeForm.options = "";
      this.attributeForm.defaultValue = "";
      if (this.attribute) {
        this.attribute = { ...this.attribute, options: null };
      }
      this.resetOptionsState();
    },

    onOptionsTextInput() {
      if (!this.hasLargeOptions) {
        this.syncOptionsCountCache();
        this.primeDefaultValueOptions();
      }
    },

    async handleSaveAttribute() {
      const nameIssue = fieldLimitIssue(
        this.attributeForm.name,
        FIELD_LIMITS.attributeName,
      );
      if (nameIssue === "empty") {
        displaySonnerError(
          this.$t("pages.storeAttributesPage.validation.nameRequired"),
        );
        return;
      }
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeName }),
        );
        return;
      }
      const keyIssue = fieldLimitIssue(
        this.attributeForm.key,
        FIELD_LIMITS.attributeKey,
      );
      if (keyIssue === "empty") {
        displaySonnerError(
          this.$t("pages.storeAttributesPage.validation.keyRequired"),
        );
        return;
      }
      if (keyIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeKey }),
        );
        return;
      }
      if (
        fieldLimitIssue(
          this.attributeForm.description,
          FIELD_LIMITS.attributeDescription,
          { required: false },
        ) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeDescription }),
        );
        return;
      }
      if (
        fieldLimitIssue(
          this.attributeForm.defaultValue,
          FIELD_LIMITS.attributeDefault,
          { required: false },
        ) === "tooLong"
      ) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeDefault }),
        );
        return;
      }

      this.isSaving = true;
      const attributeData = storeAttributeSavePayload(this.attributeForm, {
        optionsJson: this.attributeOptionsJson,
        previousOptions: this.attribute?.options || null,
      });

      await this.updateStoreAttribute({
        id: this.attributeId,
        attribute: attributeData,
        onSuccess: () => {
          displaySonnerSuccess(
            this.$t("pages.storeAttributesPage.toast.updated"),
          );
          this.attribute = { ...this.attribute, ...attributeData };
          setPageTitle(attributeData.name);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isSaving = false;
    },
  },
};
</script>

<template>
  <AppPageChrome>
    <PageSection :loading="isLoading" class="!min-h-0">
      <EmptyState
        v-if="!isLoading && !attribute"
        :title="$t('pages.storeAttributesPage.notFoundTitle')"
        :description="$t('pages.storeAttributesPage.notFoundDescription')"
        class="py-10"
      >
        <template #icon>
          <Settings2 />
        </template>
        <Button variant="outline" @click="$router.push(PAGE_STORE_ATTRIBUTES)">
          {{ $t("pages.storeAttributesPage.breadcrumb") }}
        </Button>
      </EmptyState>

      <div v-else-if="attribute" class="max-w-2xl space-y-1">
        <SettingsFormSection
          :title="$t('pages.storeAttributesPage.dialog.sectionBasic')"
        >
          <div class="grid gap-4 sm:grid-cols-2">
            <SettingsFormField
              :label="$t('pages.storeAttributesPage.dialog.nameLabel')"
              label-for="store-attr-name"
            >
              <Input
                id="store-attr-name"
                v-model="attributeForm.name"
                :placeholder="
                  $t('pages.storeAttributesPage.dialog.namePlaceholder')
                "
                :maxlength="FIELD_LIMITS.attributeName"
              />
            </SettingsFormField>
            <SettingsFormField
              :label="$t('pages.storeAttributesPage.dialog.keyLabel')"
              label-for="store-attr-key"
            >
              <template #description>{{
                $t("pages.storeAttributesPage.dialog.keyDescription")
              }}</template>
              <Input
                id="store-attr-key"
                v-model="attributeForm.key"
                placeholder="brand, country, sku"
                class="font-mono text-sm"
                :maxlength="FIELD_LIMITS.attributeKey"
              />
            </SettingsFormField>
          </div>
          <SettingsFormField
            :label="$t('pages.storeAttributesPage.dialog.typeLabel')"
            label-for="store-attr-type"
          >
            <Select v-model="attributeForm.type">
              <SelectTrigger id="store-attr-type" class="w-full">
                <SelectValue :placeholder="attributeTypeLabel" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="typeValue in STORE_ATTRIBUTE_TYPE_VALUES"
                  :key="typeValue"
                  :value="typeValue"
                >
                  {{ getAttributeTypeLabel(typeValue) }}
                </SelectItem>
              </SelectContent>
            </Select>
          </SettingsFormField>
          <template #footer>
            <Button @click="handleSaveAttribute" :disabled="isSaving">
              <Spinner v-if="isSaving" class="mr-2" />
              <Save v-else class="size-4 mr-2" />
              {{ $t("common.save") }}
            </Button>
          </template>
        </SettingsFormSection>

        <SettingsFormSection
          v-if="showOptionsField"
          :title="$t('pages.storeAttributesPage.dialog.sectionOptions')"
        >
          <template #actions>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              :disabled="!editingOptionsCount"
              @click="exportAttributeOptions"
            >
              <Download class="size-3.5 mr-1.5" />
              {{ $t("pages.storeAttributesPage.dialog.exportButton") }}
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              @click="triggerOptionsImport"
            >
              <Upload class="size-3.5 mr-1.5" />
              {{ $t("pages.storeAttributesPage.dialog.importButton") }}
            </Button>
          </template>
          <input
            ref="optionsFileInput"
            type="file"
            accept=".json,.csv,.txt,application/json,text/plain,text/csv"
            class="hidden"
            @change="handleOptionsImport"
          />

          <InsetGroup v-if="hasLargeOptions">
            <InsetGroupItem as="div">
              <p>
                <span class="tabular-nums font-medium">
                  {{ editingOptionsCount.toLocaleString() }}
                </span>
                <span class="text-muted-foreground">
                  {{
                    $t("pages.storeAttributesPage.dialog.optionsCountSuffix")
                  }}
                </span>
              </p>
              <p class="text-muted-foreground mt-0.5 text-xs font-normal">
                {{ $t("pages.storeAttributesPage.dialog.optionsStoredHint") }}
              </p>
              <template #trailing>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  @click="clearAttributeOptions"
                >
                  <Trash2 class="size-3.5 mr-1.5" />
                  {{ $t("pages.storeAttributesPage.dialog.clearButton") }}
                </Button>
              </template>
            </InsetGroupItem>
          </InsetGroup>
          <Textarea
            v-else
            v-model="attributeForm.options"
            :placeholder="
              $t('pages.storeAttributesPage.dialog.optionsPlaceholder')
            "
            class="min-h-24 resize-none font-mono text-sm"
            @input="onOptionsTextInput"
          />
          <p class="text-xs text-muted-foreground">
            {{ $t("pages.storeAttributesPage.dialog.optionsTextareaHint") }}
          </p>
          <SettingsFormField
            :label="
              $t('pages.storeAttributesPage.dialog.allowCustomValuesLabel')
            "
            label-for="store-attr-allow-custom"
            inline
            :description="
              $t('pages.storeAttributesPage.dialog.allowCustomValuesHint')
            "
          >
            <Switch
              id="store-attr-allow-custom"
              :model-value="attributeForm.allowCustomValues"
              @update:model-value="attributeForm.allowCustomValues = $event"
            />
          </SettingsFormField>
          <SettingsFormField
            :label="$t('pages.storeAttributesPage.dialog.maxValuesLabel')"
            label-for="store-attr-max-values"
          >
            <Input
              id="store-attr-max-values"
              v-model="attributeForm.maxValues"
              type="number"
              min="1"
              inputmode="numeric"
              :placeholder="
                $t('pages.storeAttributesPage.dialog.maxValuesPlaceholder')
              "
            />
            <template #description>
              {{ $t("pages.storeAttributesPage.dialog.maxValuesHint") }}
            </template>
          </SettingsFormField>
        </SettingsFormSection>

        <SettingsFormSection
          :title="$t('pages.storeAttributesPage.dialog.sectionAdditional')"
        >
          <SettingsFormField
            :label="$t('pages.storeAttributesPage.dialog.defaultValueLabel')"
            label-for="store-attr-default"
          >
            <div v-if="typeValue === 2" class="flex items-center gap-2 h-9">
              <Switch
                id="store-attr-default"
                :model-value="attributeForm.defaultValue === 'true'"
                @update:model-value="
                  attributeForm.defaultValue = $event ? 'true' : 'false'
                "
              />
              <span class="text-sm text-muted-foreground">
                {{
                  attributeForm.defaultValue === "true"
                    ? $t("common.yes")
                    : $t("common.no")
                }}
              </span>
            </div>
            <div
              v-else-if="typeValue === 3 && editingOptionsCount"
              class="space-y-1.5"
            >
              <SearchableSelect
                id="store-attr-default"
                :model-value="attributeForm.defaultValue"
                :options="defaultValueSelectOptions"
                :disabled="isLoadingDefaultValueOptions"
                :placeholder="
                  $t('pages.storeAttributesPage.dialog.notSelectedPlaceholder')
                "
                :allow-create="!!attributeForm.allowCustomValues"
                class="w-full"
                @update:model-value="attributeForm.defaultValue = $event"
                @open="loadDefaultValueOptionsIfNeeded"
              />
              <p
                v-if="hasLargeOptions && !defaultValueOptionsReady"
                class="text-xs text-muted-foreground"
              >
                {{ $t("pages.storeAttributesPage.dialog.optionsWillLoadHint") }}
              </p>
            </div>
            <Input
              v-else
              id="store-attr-default"
              v-model="attributeForm.defaultValue"
              :placeholder="
                typeValue === 4
                  ? $t('pages.storeAttributesPage.dialog.multiValuePlaceholder')
                  : $t('pages.storeAttributesPage.dialog.optionalPlaceholder')
              "
              :type="typeValue === 1 ? 'number' : 'text'"
              :maxlength="FIELD_LIMITS.attributeDefault"
            />
            <template
              v-if="typeValue === 4 && editingOptionsCount"
              #description
            >
              {{ $t("pages.storeAttributesPage.dialog.multiValueHint") }}
            </template>
          </SettingsFormField>

          <SettingsFormField
            :label="$t('pages.storeAttributesPage.dialog.descriptionLabel')"
            label-for="store-attr-description"
          >
            <Textarea
              id="store-attr-description"
              v-model="attributeForm.description"
              :placeholder="
                $t('pages.storeAttributesPage.dialog.descriptionPlaceholder')
              "
              class="min-h-20 resize-none"
              :maxlength="FIELD_LIMITS.attributeDescription"
            />
          </SettingsFormField>

          <SettingsFormField
            :label="
              $t('pages.storeAttributesPage.dialog.descriptionInTooltipLabel')
            "
            label-for="store-attr-description-tooltip"
            inline
            :description="
              $t('pages.storeAttributesPage.dialog.descriptionInTooltipHint')
            "
          >
            <Switch
              id="store-attr-description-tooltip"
              :model-value="attributeForm.descriptionInTooltip"
              @update:model-value="attributeForm.descriptionInTooltip = $event"
            />
          </SettingsFormField>

          <SettingsFormField
            v-if="isTextType"
            :label="$t('pages.storeAttributesPage.dialog.multilineLabel')"
            label-for="store-attr-multiline"
            inline
            :description="$t('pages.storeAttributesPage.dialog.multilineHint')"
          >
            <Switch
              id="store-attr-multiline"
              :model-value="attributeForm.isMultiline"
              @update:model-value="attributeForm.isMultiline = $event"
            />
          </SettingsFormField>

          <SettingsFormField
            v-if="!isNumberType"
            :label="$t('pages.storeAttributesPage.dialog.valueMaxLengthLabel')"
            label-for="store-attr-max-length"
          >
            <Input
              id="store-attr-max-length"
              v-model="attributeForm.maxLength"
              type="number"
              min="1"
              inputmode="numeric"
              :placeholder="
                $t('pages.storeAttributesPage.dialog.valueMaxLengthPlaceholder')
              "
            />
            <template #description>
              {{ $t("pages.storeAttributesPage.dialog.valueMaxLengthHint") }}
            </template>
          </SettingsFormField>

          <template v-if="isNumberType">
            <SettingsFormField
              :label="$t('pages.storeAttributesPage.dialog.minValueLabel')"
              label-for="store-attr-min-value"
            >
              <Input
                id="store-attr-min-value"
                v-model="attributeForm.minValue"
                type="text"
                inputmode="decimal"
                :placeholder="
                  $t('pages.storeAttributesPage.dialog.minValuePlaceholder')
                "
              />
            </SettingsFormField>
            <SettingsFormField
              :label="$t('pages.storeAttributesPage.dialog.maxValueLabel')"
              label-for="store-attr-max-value"
            >
              <Input
                id="store-attr-max-value"
                v-model="attributeForm.maxValue"
                type="text"
                inputmode="decimal"
                :placeholder="
                  $t('pages.storeAttributesPage.dialog.maxValuePlaceholder')
                "
              />
            </SettingsFormField>
          </template>

          <SettingsFormField
            v-if="isTextType"
            :label="
              $t('pages.storeAttributesPage.dialog.validationPatternLabel')
            "
            label-for="store-attr-pattern"
          >
            <Input
              id="store-attr-pattern"
              v-model="attributeForm.validationPattern"
              :placeholder="
                $t(
                  'pages.storeAttributesPage.dialog.validationPatternPlaceholder',
                )
              "
              :maxlength="500"
            />
            <template #description>
              {{ $t("pages.storeAttributesPage.dialog.validationPatternHint") }}
            </template>
          </SettingsFormField>

          <SettingsFormField
            :label="
              $t('pages.storeAttributesPage.dialog.showOnProductPageLabel')
            "
            label-for="store-attr-visible"
            inline
            :description="
              attributeForm.isRequired
                ? $t(
                    'pages.storeAttributesPage.dialog.showOnProductPageRequiredHint',
                  )
                : $t('pages.storeAttributesPage.dialog.showOnProductPageHint')
            "
          >
            <Switch
              id="store-attr-visible"
              :model-value="
                attributeForm.isRequired || attributeForm.showOnProductPage
              "
              :disabled="attributeForm.isRequired"
              @update:model-value="setShowOnProductPage"
            />
          </SettingsFormField>

          <SettingsFormField
            :label="$t('pages.storeAttributesPage.dialog.requiredFieldLabel')"
            label-for="store-attr-required"
            inline
            :description="
              $t('pages.storeAttributesPage.dialog.requiredFieldHint')
            "
          >
            <Switch
              id="store-attr-required"
              :model-value="attributeForm.isRequired"
              @update:model-value="setAttributeRequired"
            />
          </SettingsFormField>

          <SettingsFormField
            :label="$t('pages.storeAttributesPage.dialog.uniqueFieldLabel')"
            label-for="store-attr-unique"
            inline
            :description="
              $t('pages.storeAttributesPage.dialog.uniqueFieldHint')
            "
          >
            <Switch
              id="store-attr-unique"
              :model-value="attributeForm.isUnique"
              @update:model-value="attributeForm.isUnique = $event"
            />
          </SettingsFormField>
        </SettingsFormSection>
      </div>
    </PageSection>
  </AppPageChrome>
</template>
