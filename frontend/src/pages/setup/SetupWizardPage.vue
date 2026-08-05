<script>
import { Spinner } from "@/components/ui/spinner";
import AuthLayout from "@/components/auth/AuthLayout.vue";
import BrandingForm from "@/components/settings/BrandingForm.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  CheckCircle2,
  AlertTriangle,
} from "lucide-vue-next";
import { PAGE_PRODUCTS } from "@/router";
import { invalidateMeCache, loadMe } from "@/main";
import { clearSetupStoreCountCache } from "@/lib/setupStoreCount";

const STEP_WELCOME = "welcome";
const STEP_BRANDING = "branding";
const STEP_STORE = "store";
const STEP_DONE = "done";

const STEPS = [STEP_WELCOME, STEP_BRANDING, STEP_STORE, STEP_DONE];

export default {
  components: {
    Spinner,
    AuthLayout,
    BrandingForm,
    Button,
    Input,
    FieldGroup,
    SettingsFormField,
    Badge,
    Alert,
    AlertDescription,
    AlertTitle,
    Separator,
    Skeleton,
    SearchableSelect,
    CheckCircle2,
    AlertTriangle
  },
  data() {
    return {
      step: STEP_WELCOME,
      loading: true,
      submitting: false,
      brandingSaving: false,
      brandingFormLoading: false,
      skippedCatalogs: false,
      status: null,
      currencies: [],
      timezones: [],
      storeName: "",
      baseCurrency: "",
      timezone: "",
      createdStoreId: null,
      createdStoreName: "",
      STEPS,
      STEP_WELCOME,
      STEP_BRANDING,
      STEP_STORE,
      STEP_DONE,
    };
  },
  computed: {
    pageTitle() {
      if (this.step === STEP_BRANDING) return this.$t("pages.setup.stepBrandingTitle");
      if (this.step === STEP_STORE) return this.$t("pages.setup.stepStoreTitle");
      if (this.step === STEP_DONE) return this.$t("pages.setup.stepDoneTitle");
      return this.$t("pages.setup.stepWelcomeTitle");
    },
    catalogsReady() {
      return this.status?.ready === true;
    },
    statusBadgeLabel() {
      if (!this.status) return this.$t("pages.setup.statusUnknown");
      if (this.status.ready) return this.$t("pages.setup.statusReady");
      if (this.status.linked) return this.$t("pages.setup.statusLinkedNotReady");
      return this.$t("pages.setup.statusNotLinked");
    },
    statusBadgeVariant() {
      if (this.status?.ready) return "default";
      return "secondary";
    },
    catalogsAlertBody() {
      if (!this.status) return this.$t("pages.setup.catalogsNotReadyUnknown");
      if (this.status.linked) return this.$t("pages.setup.catalogsNotReadyLinked");
      return this.$t("pages.setup.catalogsNotReadyUnlinked");
    },
    canCreateStore() {
      if (!this.storeName.trim()) return false;
      if (this.skippedCatalogs) return true;
      return Boolean(this.baseCurrency && this.timezone);
    },
    currencyOptions() {
      return this.currencies.map((c) => ({
        value: c.code || c.value,
        label: c.label ? `${c.code || c.value} — ${c.label}` : c.code || c.value,
      }));
    },
    timezoneOptions() {
      return this.timezones.map((t) => ({
        value: t.value || t.id,
        label: t.label || t.id || t.value,
      }));
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
    ]),
    stepLabel(id) {
      if (id === STEP_BRANDING) return this.$t("pages.setup.stepBrandingShort");
      if (id === STEP_STORE) return this.$t("pages.setup.stepStoreShort");
      if (id === STEP_DONE) return this.$t("pages.setup.stepDoneShort");
      return this.$t("pages.setup.stepWelcomeShort");
    },
    stepState(id) {
      const current = STEPS.indexOf(this.step);
      const index = STEPS.indexOf(id);
      if (index < current) return "done";
      if (index === current) return "current";
      return "upcoming";
    },
    unwrap(response) {
      return response?.data?.value ?? response?.data ?? null;
    },
    async bootstrap() {
      this.loading = true;
      try {
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
                const rub = this.currencies.find((c) => (c.code || "").toUpperCase() === "RUB");
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
                this.timezone = (moscow || this.timezones[0]).value || (moscow || this.timezones[0]).id;
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
    async retryCatalogs() {
      await this.bootstrap();
    },
    goBrandingStep() {
      this.skippedCatalogs = false;
      this.step = STEP_BRANDING;
    },
    skipCatalogs() {
      this.skippedCatalogs = true;
      this.baseCurrency = "";
      this.timezone = "";
      this.step = STEP_BRANDING;
    },
    goWelcome() {
      this.step = STEP_WELCOME;
    },
    backToBranding() {
      this.step = STEP_BRANDING;
    },
    skipBranding() {
      this.step = STEP_STORE;
    },
    onBrandingLoaded() {
      this.brandingFormLoading = false;
    },
    async continueBranding() {
      if (this.brandingSaving) return;
      const form = this.$refs.brandingForm;
      if (!form) {
        this.step = STEP_STORE;
        return;
      }
      this.brandingSaving = true;
      try {
        const data = await form.save({ quiet: true });
        if (data) this.step = STEP_STORE;
      } finally {
        this.brandingSaving = false;
      }
    },
    async submitStore() {
      if (!this.canCreateStore || this.submitting) return;
      this.submitting = true;
      const payload = {
        name: this.storeName.trim(),
        addAllUsers: false,
        onSuccess: async (response) => {
          const store = this.unwrap(response);
          this.createdStoreId = store?.id ?? store?.Id ?? null;
          this.createdStoreName = store?.name || this.storeName.trim();
          clearSetupStoreCountCache();
          if (this.createdStoreId != null) {
            await this.selectCreatedStore(this.createdStoreId);
          }
          displaySonnerSuccess(this.$t("pages.setup.storeCreated"));
          this.step = STEP_DONE;
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
    openProducts() {
      this.$router.push(PAGE_PRODUCTS);
    },
  },
};
</script>

<template>
  <AuthLayout :title="pageTitle" wide>
    <nav
      class="flex items-center gap-1.5 text-xs"
      :aria-label="$t('pages.setup.stepsAriaLabel')"
    >
      <template v-for="(id, index) in STEPS" :key="id">
        <span
          class="truncate transition-colors"
          :class="{
            'font-medium text-foreground': stepState(id) === 'current',
            'text-muted-foreground': stepState(id) === 'done',
            'text-muted-foreground/50': stepState(id) === 'upcoming',
          }"
        >
          {{ stepLabel(id) }}
        </span>
        <Separator
          v-if="index < STEPS.length - 1"
          class="mx-0.5 h-3 w-px shrink-0"
          orientation="vertical"
        />
      </template>
    </nav>

    <Separator />

    <div v-if="loading" class="flex flex-col gap-3 py-1" aria-busy="true">
      <Skeleton class="h-4 w-full" />
      <Skeleton class="h-4 w-3/4" />
      <Skeleton class="h-8 w-28" />
      <Skeleton class="h-16 w-full" />
      <Skeleton class="h-9 w-full" />
    </div>

    <template v-else-if="step === STEP_WELCOME">
      <p class="text-sm text-muted-foreground leading-relaxed">
        {{ $t("pages.setup.welcomeBody") }}
      </p>

      <div class="flex flex-wrap items-center gap-2">
        <Badge :variant="statusBadgeVariant">{{ statusBadgeLabel }}</Badge>
        <span v-if="status" class="text-xs text-muted-foreground">
          {{
            $t("pages.setup.statusCounts", {
              currencies: status.currenciesCount ?? 0,
              timezones: status.timezonesCount ?? 0,
            })
          }}
        </span>
      </div>

      <Alert v-if="!catalogsReady" variant="default" class="border-amber-500/40">
        <AlertTriangle class="size-4" />
        <AlertTitle>{{ $t("pages.setup.catalogsNotReadyTitle") }}</AlertTitle>
        <AlertDescription class="text-muted-foreground">
          {{ catalogsAlertBody }}
        </AlertDescription>
      </Alert>

      <div class="flex flex-col gap-2 pt-1">
        <Button
          class="w-full"
          :disabled="!catalogsReady"
          @click="goBrandingStep"
        >
          {{ $t("pages.setup.continueCreateStore") }}
        </Button>
        <Button
          v-if="!catalogsReady"
          variant="outline"
          class="w-full"
          @click="retryCatalogs"
        >
          {{ $t("common.refresh") }}
        </Button>
        <Button
          v-if="!catalogsReady"
          variant="ghost"
          class="w-full"
          @click="skipCatalogs"
        >
          {{ $t("pages.setup.skipCatalogs") }}
        </Button>
      </div>
    </template>

    <template v-else-if="step === STEP_BRANDING">
      <p class="text-sm text-muted-foreground leading-relaxed">
        {{ $t("pages.setup.brandingBody") }}
      </p>

      <div class="max-h-[50vh] overflow-y-auto pr-1">
        <BrandingForm
          ref="brandingForm"
          compact
          load-on-mount
          @loaded="onBrandingLoaded"
        />
      </div>

      <div class="flex flex-col gap-2 pt-1">
        <Button
          class="w-full"
          :disabled="brandingSaving"
          @click="continueBranding"
        >
          <Spinner v-if="brandingSaving" />
          {{ $t("pages.setup.brandingContinue") }}
        </Button>
        <Button
          variant="ghost"
          class="w-full"
          :disabled="brandingSaving"
          @click="skipBranding"
        >
          {{ $t("pages.setup.brandingSkip") }}
        </Button>
        <Button
          variant="ghost"
          class="w-full"
          :disabled="brandingSaving"
          @click="goWelcome"
        >
          {{ $t("common.back") }}
        </Button>
      </div>
    </template>

    <template v-else-if="step === STEP_STORE">
      <FieldGroup class="gap-4">
        <SettingsFormField
          :label="$t('pages.setup.storeNameLabel')"
          label-for="setup-store-name"
        >
          <Input
            id="setup-store-name"
            v-model="storeName"
            :placeholder="$t('pages.setup.storeNamePlaceholder')"
            autocomplete="organization"
            :disabled="submitting"
          />
        </SettingsFormField>

        <template v-if="!skippedCatalogs">
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

        <p
          v-else
          class="text-xs text-muted-foreground leading-relaxed"
        >
          {{ $t("pages.setup.skippedCatalogsHint") }}
        </p>

        <div class="flex flex-col gap-2 pt-1">
          <Button
            class="w-full"
            :disabled="!canCreateStore || submitting"
            @click="submitStore"
          >
            <Spinner v-if="submitting" />
            {{ $t("pages.setup.createStore") }}
          </Button>
          <Button
            variant="ghost"
            class="w-full"
            :disabled="submitting"
            @click="backToBranding"
          >
            {{ $t("common.back") }}
          </Button>
        </div>
      </FieldGroup>
    </template>

    <template v-else>
      <div class="flex flex-col items-center gap-3 text-center py-2">
        <CheckCircle2 class="size-10 text-foreground" />
        <p class="text-sm text-muted-foreground leading-relaxed">
          {{
            $t("pages.setup.doneBody", {
              name: createdStoreName || $t("pages.setup.yourStore"),
            })
          }}
        </p>
      </div>
      <Button class="w-full" @click="openProducts">
        {{ $t("pages.setup.openProducts") }}
      </Button>
      <p class="text-xs text-center text-muted-foreground">
        {{ $t("pages.setup.marketplaceLaterHint") }}
      </p>
    </template>
  </AuthLayout>
</template>
