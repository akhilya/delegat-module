<script>
import { Spinner } from "@/components/ui/spinner";
import SetupLayout from "@/components/setup/SetupLayout.vue";
import BrandingForm from "@/components/settings/BrandingForm.vue";
import ConnectionCreateForm from "@/components/marketplace/ConnectionCreateForm.vue";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
  apiErrorCode,
} from "@/store/sonnerHelper";
import { Check, Plus } from "lucide-vue-next";
import { PAGE_PRODUCTS, PAGE_CATALOG_CONNECT } from "@/router";
import { invalidateMeCache, loadMe } from "@/main";
import {
  clearSetupStoreCountCache,
  fetchSetupState,
} from "@/lib/setupStoreCount";
import { adapterCanImportAll } from "@/lib/adapterCapabilities";
import { FIELD_LIMITS, isNonEmptyWithinLimit } from "@/lib/fieldLimits";

const STEP_WELCOME = "welcome";
const STEP_STORE = "store";
const STEP_BRANDING = "branding";
const STEP_ADAPTERS = "adapters";
const STEP_DONE = "done";

const STEPS = [
  STEP_WELCOME,
  STEP_STORE,
  STEP_BRANDING,
  STEP_ADAPTERS,
  STEP_DONE,
];

export default {
  components: {
    Spinner,
    SetupLayout,
    BrandingForm,
    ConnectionCreateForm,
    AdapterIcon,
    Button,
    Input,
    FieldGroup,
    SettingsFormField,
    SearchableSelect,
    Check,
    Plus,
  },
  data() {
    return {
      step: STEP_WELCOME,
      loading: true,
      submitting: false,
      brandingSaving: false,
      skippedCatalogs: false,
      status: null,
      currencies: [],
      timezones: [],
      storeName: "",
      baseCurrency: "",
      timezone: "",
      createdStoreId: null,
      createdStoreName: "",
      connections: [],
      adapters: [],
      adaptersLoading: false,
      creatingConnection: false,
      connectionQuotaReached: false,
      FIELD_LIMITS,
      STEPS,
      STEP_WELCOME,
      STEP_BRANDING,
      STEP_STORE,
      STEP_ADAPTERS,
      STEP_DONE,
    };
  },
  computed: {
    stepIndex() {
      return STEPS.indexOf(this.step);
    },
    stepLabels() {
      return STEPS.map((id) => this.stepLabel(id));
    },
    pageTitle() {
      if (this.step === STEP_WELCOME)
        return this.$t("pages.setup.welcomeTitle");
      if (this.step === STEP_BRANDING)
        return this.$t("pages.setup.stepBrandingTitle");
      if (this.step === STEP_STORE)
        return this.$t("pages.setup.stepStoreTitle");
      if (this.step === STEP_ADAPTERS)
        return this.$t("pages.setup.stepAdaptersTitle");
      return this.$t("pages.setup.stepDoneTitle");
    },
    pageSubtitle() {
      if (this.step === STEP_WELCOME) return this.$t("pages.setup.welcomeBody");
      if (this.step === STEP_STORE) return this.$t("pages.setup.storeBody");
      if (this.step === STEP_BRANDING)
        return this.$t("pages.setup.brandingBody");
      if (this.step === STEP_ADAPTERS)
        return this.$t("pages.setup.adaptersBody");
      return this.$t("pages.setup.doneBody", {
        name: this.createdStoreName || this.$t("pages.setup.yourStore"),
      });
    },
    catalogsReady() {
      return this.status?.ready === true;
    },
    hasSelectableAdapters() {
      if (this.connectionQuotaReached) return false;
      return this.adapters.length > 0;
    },
    showStoreCatalogs() {
      return !this.skippedCatalogs;
    },
    canCreateStore() {
      if (!isNonEmptyWithinLimit(this.storeName, FIELD_LIMITS.storeName))
        return false;
      if (this.skippedCatalogs) return true;
      return Boolean(this.baseCurrency && this.timezone);
    },
    storeNameError() {
      const name = this.storeName.trim();
      if (!name) return "";
      if (name.length > FIELD_LIMITS.storeName) {
        return this.$t("common.tooLong", { max: FIELD_LIMITS.storeName });
      }
      return "";
    },
    setupFormId() {
      if (this.step === STEP_STORE) return "setup-store-form";
      if (this.step === STEP_BRANDING) return "setup-branding-form";
      return "";
    },
    currencyOptions() {
      return this.currencies.map((c) => ({
        value: c.code || c.value,
        label: c.label
          ? `${c.code || c.value} · ${c.label}`
          : c.code || c.value,
      }));
    },
    timezoneOptions() {
      return this.timezones.map((t) => ({
        value: t.value || t.id,
        label: t.label || t.id || t.value,
      }));
    },
    hasImportCapableConnection() {
      return this.connections.some((conn) => {
        const adapter = this.adapters.find((a) => a.key === conn.adapterKey);
        return adapterCanImportAll(adapter || conn);
      });
    },
    showBack() {
      return (
        this.step === STEP_STORE ||
        this.step === STEP_ADAPTERS ||
        this.step === STEP_DONE
      );
    },
    continueLabel() {
      if (this.step === STEP_DONE) {
        return this.hasImportCapableConnection
          ? this.$t("pages.setup.openCatalogConnect")
          : this.$t("pages.setup.openProducts");
      }
      return this.$t("pages.setup.continue");
    },
    continueDisabled() {
      if (this.step === STEP_STORE) return !this.canCreateStore;
      if (this.step === STEP_ADAPTERS) return this.creatingConnection;
      return false;
    },
    continueLoading() {
      if (this.step === STEP_STORE || this.step === STEP_DONE)
        return this.submitting;
      if (this.step === STEP_BRANDING) return this.brandingSaving;
      return false;
    },
    skipLabel() {
      if (this.step === STEP_BRANDING)
        return this.$t("pages.setup.brandingSkip");
      return "";
    },
  },
  async mounted() {
    await this.bootstrap();
  },
  methods: {
    ...mapActions([
      "getReferenceStatus",
      "getReferenceCurrencies",
      "getReferenceTimezones",
      "createStore",
      "postSelectStore",
      "completeStoreOnboarding",
      "getConnections",
      "getAvailableAdapters",
      "createConnection",
    ]),
    stepLabel(id) {
      if (id === STEP_BRANDING) return this.$t("pages.setup.stepBrandingShort");
      if (id === STEP_STORE) return this.$t("pages.setup.stepStoreShort");
      if (id === STEP_ADAPTERS) return this.$t("pages.setup.stepAdaptersShort");
      if (id === STEP_DONE) return this.$t("pages.setup.stepDoneShort");
      return this.$t("pages.setup.stepWelcomeShort");
    },
    unwrap(response) {
      return response?.data?.value ?? response?.data ?? null;
    },
    async bootstrap() {
      this.loading = true;
      try {
        const setupState = await fetchSetupState({ force: true });
        if (setupState.incompleteStore) {
          this.createdStoreId = setupState.incompleteStore.id;
          this.createdStoreName = setupState.incompleteStore.name;
          await this.selectCreatedStore(this.createdStoreId);
          this.step = STEP_ADAPTERS;
          await this.loadAdapterStep();
          return;
        }
        await Promise.all([this.loadStatus(), this.loadCatalogs()]);
      } finally {
        this.loading = false;
      }
    },
    loadStatus() {
      return new Promise((resolve) => {
        this.getReferenceStatus({
          onSuccess: (response) => {
            this.status = this.unwrap(response) || null;
            resolve();
          },
          onError: () => {
            this.status = null;
            resolve();
          },
        });
      });
    },
    loadCatalogs() {
      return Promise.all([
        new Promise((resolve) => {
          this.getReferenceCurrencies({
            onSuccess: (response) => {
              const items = this.unwrap(response);
              this.currencies = Array.isArray(items) ? items : [];
              if (!this.baseCurrency && this.currencies.length) {
                const rub = this.currencies.find(
                  (c) => (c.code || "").toUpperCase() === "RUB",
                );
                this.baseCurrency = (rub || this.currencies[0]).code;
              }
              resolve();
            },
            onError: () => {
              this.currencies = [];
              resolve();
            },
          });
        }),
        new Promise((resolve) => {
          this.getReferenceTimezones({
            onSuccess: (response) => {
              const items = this.unwrap(response);
              this.timezones = Array.isArray(items) ? items : [];
              if (!this.timezone && this.timezones.length) {
                const moscow = this.timezones.find(
                  (t) => (t.value || t.id) === "Europe/Moscow",
                );
                this.timezone =
                  (moscow || this.timezones[0]).value ||
                  (moscow || this.timezones[0]).id;
              }
              resolve();
            },
            onError: () => {
              this.timezones = [];
              resolve();
            },
          });
        }),
      ]);
    },
    async goStoreStep() {
      await Promise.all([this.loadStatus(), this.loadCatalogs()]);
      this.skippedCatalogs = !this.catalogsReady;
      this.step = STEP_STORE;
    },
    goWelcome() {
      this.step = STEP_WELCOME;
    },
    skipBranding() {
      this.goAdapters();
    },
    async continueBranding() {
      if (this.brandingSaving) return;
      const form = this.$refs.brandingForm;
      if (!form) {
        this.goAdapters();
        return;
      }
      this.brandingSaving = true;
      try {
        const data = await form.save({ quiet: true });
        if (data) this.goAdapters();
      } finally {
        this.brandingSaving = false;
      }
    },
    async goAdapters() {
      this.step = STEP_ADAPTERS;
      await this.loadAdapterStep();
    },
    backToBranding() {
      this.step = STEP_BRANDING;
    },
    goDone() {
      this.step = STEP_DONE;
    },
    async loadAdapterStep() {
      this.adaptersLoading = true;
      await Promise.all([this.fetchConnections(), this.fetchAdapters()]);
      this.adaptersLoading = false;
    },
    fetchConnections() {
      return new Promise((resolve) => {
        this.getConnections({
          onSuccess: (response) => {
            const data = extractResponseData(response, []);
            this.connections = Array.isArray(data) ? data : [];
            resolve();
          },
          onError: (error) => {
            displaySonnerError(error);
            this.connections = [];
            resolve();
          },
        });
      });
    },
    fetchAdapters() {
      return new Promise((resolve) => {
        this.getAvailableAdapters({
          onSuccess: (response) => {
            this.adapters = extractResponseData(response, []);
            resolve();
          },
          onError: (error) => {
            displaySonnerError(error);
            resolve();
          },
        });
      });
    },
    adapterName(adapterKey) {
      return (
        this.adapters.find((a) => a.key === adapterKey)?.displayName ||
        adapterKey
      );
    },
    async addConnection() {
      const form = this.$refs.adapterForm;
      const data = form?.payload?.();
      if (!data?.name) {
        displaySonnerError(
          this.$t("marketplace.marketplacePage.enterConnectionName"),
        );
        return;
      }
      if (!isNonEmptyWithinLimit(data.name, FIELD_LIMITS.connectionName)) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.connectionName }),
        );
        return;
      }
      if (!data.adapterKey) {
        displaySonnerError(
          this.$t("marketplace.marketplacePage.selectAdapter"),
        );
        return;
      }
      if (!form.canSubmit) {
        displaySonnerError(
          this.$t("marketplace.marketplacePage.enterCredentials"),
        );
        return;
      }
      this.creatingConnection = true;
      try {
        if (this.createdStoreId != null) {
          await this.selectCreatedStore(this.createdStoreId);
        }
        let created = null;
        await this.createConnection({
          data,
          onSuccess: (response) => {
            created = extractResponseData(response, {});
          },
          onError: (error) => {
            if (apiErrorCode(error) === "quota_connections_reached") {
              this.connectionQuotaReached = true;
            }
            displaySonnerError(error);
          },
        });
        if (!created?.id) return;
        displaySonnerSuccess(
          this.$t("marketplace.marketplacePage.connectionCreated"),
        );
        form.reset();
        await this.fetchConnections();
      } finally {
        this.creatingConnection = false;
      }
    },
    async submitStore() {
      if (!this.canCreateStore || this.submitting) return;
      this.submitting = true;
      const payload = {
        name: this.storeName.trim(),
        addAllUsers: false,
        fromSetup: true,
        onSuccess: async (response) => {
          const store = this.unwrap(response);
          this.createdStoreId = store?.id ?? store?.Id ?? null;
          this.createdStoreName = store?.name || this.storeName.trim();
          clearSetupStoreCountCache();
          if (this.createdStoreId != null) {
            await this.selectCreatedStore(this.createdStoreId);
          }
          this.step = STEP_BRANDING;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      };
      if (!this.skippedCatalogs) {
        payload.baseCurrency = this.baseCurrency;
        payload.timezone = this.timezone;
      }
      await this.createStore(payload);
      this.submitting = false;
    },
    selectCreatedStore(storeId) {
      return new Promise((resolve) => {
        this.postSelectStore({
          storeId,
          onSuccess: async () => {
            invalidateMeCache();
            await loadMe();
            resolve();
          },
          onError: (error) => {
            displaySonnerError(error);
            resolve();
          },
        });
      });
    },
    async finishSetup() {
      if (this.submitting) return;
      this.submitting = true;
      try {
        if (this.createdStoreId != null) {
          let completed = false;
          await this.completeStoreOnboarding({
            storeId: this.createdStoreId,
            onSuccess: () => {
              completed = true;
            },
            onError: (error) => displaySonnerError(error),
          });
          if (!completed) return;
          clearSetupStoreCountCache();
        }
        if (this.hasImportCapableConnection) {
          this.$router.push({
            path: PAGE_CATALOG_CONNECT,
            query: { setupPipeline: "1" },
          });
        } else {
          this.$router.push(PAGE_PRODUCTS);
        }
      } finally {
        this.submitting = false;
      }
    },
    onBack() {
      if (this.step === STEP_STORE) this.goWelcome();
      else if (this.step === STEP_ADAPTERS) this.backToBranding();
      else if (this.step === STEP_DONE) this.step = STEP_ADAPTERS;
    },
    onContinue() {
      if (this.step === STEP_WELCOME) this.goStoreStep();
      else if (this.step === STEP_STORE) this.submitStore();
      else if (this.step === STEP_BRANDING) this.continueBranding();
      else if (this.step === STEP_ADAPTERS) this.goDone();
      else this.finishSetup();
    },
  },
};
</script>

<template>
  <SetupLayout
    :title="pageTitle"
    :subtitle="pageSubtitle"
    :loading="loading"
    :welcome="step === STEP_WELCOME"
    :step-index="stepIndex"
    :step-count="STEPS.length"
    :steps-aria-label="$t('pages.setup.stepsAriaLabel')"
    :step-labels="stepLabels"
    :show-back="showBack"
    :back-label="$t('common.back')"
    :continue-label="continueLabel"
    :continue-disabled="continueDisabled"
    :continue-loading="continueLoading"
    :skip-label="skipLabel"
    :skip-disabled="brandingSaving"
    :step-key="step"
    :form-id="setupFormId"
    @back="onBack"
    @continue="onContinue"
    @skip="skipBranding"
  >
    <template v-if="step === STEP_STORE">
      <form
        id="setup-store-form"
        class="contents"
        @submit.prevent="submitStore"
      >
        <FieldGroup class="gap-5">
          <SettingsFormField
            :label="$t('pages.setup.storeNameLabel')"
            label-for="setup-store-name"
            :error="storeNameError"
          >
            <Input
              id="setup-store-name"
              v-model="storeName"
              :placeholder="$t('pages.setup.storeNamePlaceholder')"
              autocomplete="organization"
              :maxlength="FIELD_LIMITS.storeName"
              :disabled="submitting"
            />
          </SettingsFormField>

          <template v-if="showStoreCatalogs">
            <SettingsFormField :label="$t('pages.setup.baseCurrencyLabel')">
              <SearchableSelect
                v-model="baseCurrency"
                :options="currencyOptions"
                :placeholder="$t('pages.setup.baseCurrencyPlaceholder')"
                :disabled="submitting || !currencyOptions.length"
              />
            </SettingsFormField>

            <SettingsFormField :label="$t('pages.setup.timezoneLabel')">
              <SearchableSelect
                v-model="timezone"
                :options="timezoneOptions"
                :placeholder="$t('pages.setup.timezonePlaceholder')"
                :disabled="submitting || !timezoneOptions.length"
              />
            </SettingsFormField>
          </template>
        </FieldGroup>
      </form>
    </template>

    <template v-else-if="step === STEP_BRANDING">
      <form
        id="setup-branding-form"
        class="contents"
        @submit.prevent="continueBranding"
      >
        <BrandingForm
          ref="brandingForm"
          compact
          load-on-mount
          :store-id="createdStoreId"
        />
      </form>
    </template>

    <template v-else-if="step === STEP_ADAPTERS">
      <div v-if="adaptersLoading" class="flex justify-center py-10">
        <Spinner size="lg" />
      </div>

      <template v-else>
        <ul v-if="connections.length" class="setup-connections">
          <li
            v-for="conn in connections"
            :key="conn.id"
            class="setup-connection"
          >
            <AdapterIcon
              :adapter-key="conn.adapterKey"
              class="size-4 shrink-0"
            />
            <div class="min-w-0 flex-1">
              <p class="truncate text-sm font-medium">{{ conn.name }}</p>
              <p class="truncate text-xs text-muted-foreground">
                {{ adapterName(conn.adapterKey) }}
              </p>
            </div>
            <Check
              class="size-4 shrink-0 text-muted-foreground"
              aria-hidden="true"
            />
            <span class="sr-only">{{
              $t("pages.setup.adapterConnected")
            }}</span>
          </li>
        </ul>

        <form
          v-if="hasSelectableAdapters"
          class="contents"
          @submit.prevent="addConnection"
        >
          <ConnectionCreateForm
            ref="adapterForm"
            quiet
            :adapters="adapters"
            :disabled="creatingConnection"
            :show-active-switch="false"
            id-prefix="setup-conn"
          />

          <Button
            type="submit"
            variant="outline"
            class="w-full"
            :disabled="creatingConnection || adapters.length === 0"
          >
            <Spinner v-if="creatingConnection" />
            <Plus v-else class="size-4" />
            {{ $t("pages.setup.adapterAdd") }}
          </Button>
        </form>

        <p
          v-else-if="connectionQuotaReached"
          class="text-center text-sm text-muted-foreground"
        >
          {{ $t("pages.setup.connectionQuotaReached") }}
        </p>

        <p
          v-if="adapters.length === 0"
          class="text-center text-sm text-muted-foreground"
        >
          {{ $t("pages.setup.adaptersUnavailable") }}
        </p>
      </template>
    </template>
  </SetupLayout>
</template>

<style scoped>
.setup-connections {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin: 0;
  padding: 0;
  list-style: none;
}

.setup-connection {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 0.875rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
  background: var(--card);
}
</style>
