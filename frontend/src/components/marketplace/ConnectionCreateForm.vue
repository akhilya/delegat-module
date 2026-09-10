<script>
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import Separator from "@/components/ui/separator/Separator.vue";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import Switch from "@/components/ui/switch/Switch.vue";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import DocsHelpRow from "@/components/docs/DocsHelpRow.vue";
import { getAdapterCredentialsDocsSlug } from "@/utils/docsUrl";
import {
  FIELD_LIMITS,
  isNonEmptyWithinLimit,
} from "@/lib/fieldLimits";
import {
  defaultCredentialValues,
  normalizeCredentialFields,
} from "@/lib/credentialFields";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

export default {
  components: {
    Input,
    PasswordInput,
    SettingsFormField,
    FieldGroup,
    Separator,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    Switch,
    AdapterIcon,
    DocsHelpRow,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
  },
  props: {
    adapters: { type: Array, default: () => [] },
    disabled: { type: Boolean, default: false },
    showActiveSwitch: { type: Boolean, default: true },
    idPrefix: { type: String, default: "conn" },
    /** Hide plugin versions and schema jargon (setup wizard). */
    quiet: { type: Boolean, default: false },
    initialAdapterKey: { type: String, default: "" },
    initialName: { type: String, default: "" },
    lockAdapter: { type: Boolean, default: false },
  },
  data() {
    return {
      name: "",
      adapterKey: "",
      credentialValues: {},
      isActive: true,
      FIELD_LIMITS,
    };
  },
  computed: {
    selectedAdapterMeta() {
      if (!this.adapterKey) return null;
      return this.adapters.find((a) => a.key === this.adapterKey) || null;
    },
    selectedAdapterSchema() {
      return this.selectedAdapterMeta?.credentialsSchema || null;
    },
    credentialFields() {
      return normalizeCredentialFields(this.selectedAdapterSchema);
    },
    credentialsDocsSlug() {
      if (!this.adapterKey) return null;
      return getAdapterCredentialsDocsSlug(this.adapterKey);
    },
    canSubmit() {
      if (
        !isNonEmptyWithinLimit(this.name, FIELD_LIMITS.connectionName) ||
        !this.adapterKey
      ) {
        return false;
      }
      for (const field of this.credentialFields) {
        const value = this.credentialValues[field.key];
        if (field.isRequired && (value == null || String(value).trim() === "")) {
          return false;
        }
        if (
          value != null &&
          String(value).length > (field.maxLength || FIELD_LIMITS.credentialValue)
        ) {
          return false;
        }
      }
      return true;
    },
  },
  methods: {
    onAdapterSelect(adapterKey) {
      this.adapterKey = adapterKey || "";
      this.credentialValues = {};
      if (this.adapterKey && !this.name.trim()) {
        const meta = this.selectedAdapterMeta;
        if (meta?.displayName) this.name = meta.displayName;
      }
      this.credentialValues = defaultCredentialValues(this.credentialFields);
    },
    payload() {
      return {
        name: this.name.trim(),
        adapterKey: this.adapterKey,
        credentialsJson: JSON.stringify(this.credentialValues),
        isActive: this.isActive,
      };
    },
    reset() {
      this.name = "";
      this.adapterKey = "";
      this.credentialValues = {};
      this.isActive = true;
    },
    applyInitial() {
      if (this.initialAdapterKey && this.adapterKey !== this.initialAdapterKey) {
        this.onAdapterSelect(this.initialAdapterKey);
      }
      if (this.initialName && !this.name.trim()) {
        this.name = this.initialName;
      }
    },
  },
  watch: {
    adapters: {
      immediate: true,
      handler() {
        this.applyInitial();
      },
    },
    initialAdapterKey() {
      this.applyInitial();
    },
    credentialFields(fields) {
      if (!this.adapterKey || Object.keys(this.credentialValues).length) return;
      this.credentialValues = defaultCredentialValues(fields);
    },
  },
};
</script>

<template>
  <FieldGroup class="gap-5">
    <SettingsFormField
      :label="
        quiet
          ? $t('pages.setup.connectionNameLabel')
          : $t('marketplace.marketplacePage.connectionNameLabel')
      "
      :label-for="`${idPrefix}-name`"
    >
      <Input
        :id="`${idPrefix}-name`"
        v-model="name"
        :placeholder="
          $t('marketplace.marketplacePage.connectionNamePlaceholder')
        "
        autocomplete="off"
        :maxlength="FIELD_LIMITS.connectionName"
        :disabled="disabled"
      />
    </SettingsFormField>

    <SettingsFormField
      v-if="!(lockAdapter && adapterKey)"
      :label="
        quiet
          ? $t('pages.setup.marketplaceLabel')
          : $t('marketplace.marketplacePage.adapterLabel')
      "
    >
      <InsetGroup
        role="radiogroup"
        :aria-label="$t('marketplace.marketplacePage.adapterLabel')"
      >
        <InsetGroupItem
          v-for="adapter in adapters"
          :key="adapter.key"
          role="radio"
          :aria-checked="adapterKey === adapter.key"
          :selected="adapterKey === adapter.key"
          :disabled="disabled"
          @click="onAdapterSelect(adapter.key)"
        >
          <template #leading>
            <AdapterIcon :adapter-key="adapter.key" class="size-4" />
          </template>
          <p class="truncate">{{ adapter.displayName }}</p>
          <p
            v-if="adapter.version && !quiet"
            class="text-muted-foreground mt-0.5 font-mono text-xs font-normal tabular-nums"
          >
            v{{ adapter.version }}
          </p>
        </InsetGroupItem>
      </InsetGroup>
      <template v-if="!quiet" #description>
        {{ $t("marketplace.marketplacePage.adapterPickHint") }}
      </template>
    </SettingsFormField>

    <template v-if="adapterKey && credentialFields.length > 0">
      <Separator v-if="!quiet" />
      <div v-if="!quiet" class="space-y-1">
        <InsetGroupLabel class="mb-0">
          {{ $t("marketplace.marketplacePage.credentialsSectionTitle") }}
        </InsetGroupLabel>
        <p class="px-1 text-xs text-muted-foreground">
          {{
            $t("marketplace.marketplacePage.credentialsSectionHint", {
              adapter: selectedAdapterMeta?.displayName || adapterKey,
            })
          }}
        </p>
      </div>
      <DocsHelpRow
        v-if="credentialsDocsSlug"
        :slug="credentialsDocsSlug"
        :title="
          quiet
            ? $t('pages.setup.credentialsDocsLink')
            : $t('marketplace.marketplacePage.credentialsDocsLink')
        "
        :description="
          $t('marketplace.marketplacePage.credentialsDocsHint', {
            adapter: selectedAdapterMeta?.displayName || adapterKey,
          })
        "
      />
      <FieldGroup class="gap-4">
        <SettingsFormField
          v-for="field in credentialFields"
          :key="field.key"
          :label="field.displayName || field.key"
          :label-for="`${idPrefix}-cred-${field.key}`"
          :required="field.isRequired"
          :description="field.description || ''"
        >
          <PasswordInput
            v-if="field.isSecret"
            :id="`${idPrefix}-cred-${field.key}`"
            v-model="credentialValues[field.key]"
            :placeholder="field.placeholder || ''"
            autocomplete="off"
            :maxlength="field.maxLength || FIELD_LIMITS.credentialValue"
            :disabled="disabled"
          />
          <Select
            v-else-if="field.type === 'select'"
            :model-value="credentialValues[field.key]"
            :disabled="disabled"
            @update:model-value="credentialValues[field.key] = $event"
          >
            <SelectTrigger
              :id="`${idPrefix}-cred-${field.key}`"
              class="w-full"
            >
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="option in field.options"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </SelectItem>
            </SelectContent>
          </Select>
          <Input
            v-else
            :id="`${idPrefix}-cred-${field.key}`"
            v-model="credentialValues[field.key]"
            type="text"
            :placeholder="field.placeholder || ''"
            autocomplete="off"
            :maxlength="field.maxLength || FIELD_LIMITS.credentialValue"
            :disabled="disabled"
          />
        </SettingsFormField>
      </FieldGroup>
    </template>

    <template v-if="showActiveSwitch">
      <Separator />
      <SettingsFormField
        :label="$t('marketplace.marketplacePage.activeConnectionLabel')"
        :label-for="`${idPrefix}-active`"
        inline
        :description="$t('marketplace.marketplacePage.activeConnectionHint')"
      >
        <Switch
          :id="`${idPrefix}-active`"
          :model-value="isActive"
          :disabled="disabled"
          @update:model-value="isActive = $event"
        />
      </SettingsFormField>
    </template>
  </FieldGroup>
</template>
