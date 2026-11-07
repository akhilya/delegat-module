<script>
import { Spinner } from "@/components/ui/spinner";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import NavSearchField from "@/components/NavSearchField.vue";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { loadMe, invalidateMeCache } from "@/main";
import { hasPermission, PERMS } from "@/lib/permissions";
import {
  FIELD_LIMITS,
  fieldLimitIssue,
  isNonEmptyWithinLimit,
} from "@/lib/fieldLimits";
import {
  Check,
  Plus,
  Store,
  Users,
  Save,
  Trash2,
  Coins,
} from "lucide-vue-next";
import BrandingForm from "@/components/settings/BrandingForm.vue";
import { Switch } from "@/components/ui/switch";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { SearchableSelect } from "@/components/ui/searchable-select";
import { formatDateTime } from "@/utils/dateTime";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogClose,
  DialogHeaderClose,
} from "@/components/ui/dialog";

export default {
  components: {
    Spinner,
    PageSection,
    Button,
    Input,
    NavSearchField,
    FieldGroup,
    SettingsFormField,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    Check,
    Plus,
    Store,
    Users,
    Save,
    Trash2,
    Coins,
    BrandingForm,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogFooter,
    DialogClose,
    DialogHeaderClose,
    Switch,
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    SearchableSelect,
  },
  data() {
    return {
      stores: [],
      allUsers: [],
      editingStore: null,
      editForm: {
        name: "",
        memberIds: [],
        timezone: "",
        extendedPriceParameters: false,
      },
      storeCurrencies: [],
      newCurrencyCode: "",
      newCurrencyRate: "",
      editTab: "general",
      memberQuery: "",
      isLoading: false,
      isCreating: false,
      isSaving: false,
      brandingFormDirty: false,
      brandingFormSaving: false,
      isEditDialogOpen: false,
      newStoreName: "",
      newStoreBaseCurrency: "",
      newStoreTimezone: "",
      addAllUsers: true,
      createAttempted: false,
      FIELD_LIMITS,
      showCreateDialog: false,
      timezoneOptions: [],
      addableCurrencies: [],
      catalogReady: false,
      catalogLinked: false,
      me: null,
      showDeleteDialog: false,
      deleteConfirmName: "",
      isDeleting: false,
    };
  },
  computed: {
    availableCurrencyOptions() {
      const existing = new Set((this.storeCurrencies || []).map((c) => c.code));
      return (this.addableCurrencies || []).filter(
        (c) => !existing.has(c.code),
      );
    },
    availableCurrencySelectOptions() {
      return this.availableCurrencyOptions.map((c) => ({
        value: c.code,
        label: c.label || c.code,
      }));
    },
    extraCurrencies() {
      return (this.storeCurrencies || []).filter((c) => !c.isBase);
    },
    storeBaseCurrency() {
      return (
        this.editingStore?.baseCurrency ||
        this.storeCurrencies?.find((c) => c.isBase)?.code ||
        null
      );
    },
    selectedMembersCount() {
      return this.editForm.memberIds.length;
    },
    canSubmitNewStore() {
      return isNonEmptyWithinLimit(this.newStoreName, FIELD_LIMITS.storeName);
    },
    newStoreNameError() {
      const issue = fieldLimitIssue(this.newStoreName, FIELD_LIMITS.storeName);
      if (issue === "tooLong") {
        return this.$t("common.tooLong", { max: FIELD_LIMITS.storeName });
      }
      if (this.createAttempted && issue === "empty") {
        return this.$t("pages.storesPage.nameRequiredError");
      }
      return "";
    },
    createCurrencyOptions() {
      return (this.addableCurrencies || []).map((c) => ({
        value: c.code,
        label: c.label ? `${c.code} · ${c.label}` : c.code,
      }));
    },
    showCreateCatalogs() {
      return (
        this.createCurrencyOptions.length > 0 ||
        (this.timezoneOptions || []).length > 0
      );
    },
    timezoneSelectOptions() {
      const opts = [...(this.timezoneOptions || [])];
      const current = (this.editForm.timezone || "").trim();
      if (current && !opts.some((t) => t.value === current)) {
        opts.unshift({ value: current, label: current });
      }
      return opts;
    },
    canChangeTimezone() {
      return (this.timezoneOptions || []).length > 0;
    },
    filteredUsers() {
      const q = this.memberQuery.trim().toLowerCase();
      if (!q) return this.allUsers;
      return this.allUsers.filter((u) => {
        const name = (u.userName || "").toLowerCase();
        return name.includes(q) || String(u.id).includes(q);
      });
    },
    canDeleteStore() {
      return this.me?.isAdmin === true && !!this.editingStore;
    },
    isLastStore() {
      return (this.stores || []).length <= 1;
    },
    deleteNameMatches() {
      if (!this.editingStore?.name) return false;
      return this.deleteConfirmName.trim() === this.editingStore.name;
    },
    brandingApplyLive() {
      return (
        this.editingStore?.id != null &&
        this.me?.selectedStoreId != null &&
        this.editingStore.id === this.me.selectedStoreId
      );
    },
    canCreateStores() {
      return hasPermission(this.me, PERMS.StoresCreate);
    },
  },
  watch: {
    showCreateDialog(open) {
      if (open) this.resetCreateForm();
    },
  },
  methods: {
    ...mapActions([
      "getAllStores",
      "createStore",
      "getStoreDetail",
      "getAllUsers",
      "updateStore",
      "addStoreCurrency",
      "patchStoreCurrency",
      "deleteStoreCurrency",
      "deleteStore",
      "getReferenceCurrencies",
      "getReferenceTimezones",
      "getReferenceStatus",
    ]),
    formatCurrencyDate(iso) {
      return formatDateTime(iso, this.editForm.timezone || "UTC").combined;
    },

    async fetchReferenceCatalogs() {
      await this.getReferenceStatus({
        onSuccess: (response) => {
          const status = response.data?.value ?? response.data;
          this.catalogReady = Boolean(status?.ready);
          this.catalogLinked = Boolean(status?.linked);
        },
        onError: () => {
          this.catalogReady = false;
          this.catalogLinked = false;
        },
      });
      await this.getReferenceTimezones({
        onSuccess: (response) => {
          const items = response.data?.value ?? response.data ?? [];
          this.timezoneOptions = (Array.isArray(items) ? items : []).map(
            (t) => ({
              value: t.value || t.id,
              label: t.label || t.id,
            }),
          );
        },
        onError: () => {
          this.timezoneOptions = [];
        },
      });
      await this.getReferenceCurrencies({
        onSuccess: (response) => {
          const items = response.data?.value ?? response.data ?? [];
          this.addableCurrencies = (Array.isArray(items) ? items : []).map(
            (c) => ({
              code: c.code,
              label: c.label || c.code,
            }),
          );
        },
        onError: () => {
          this.addableCurrencies = [];
        },
      });
      this.seedCreateCatalogDefaults();
    },

    seedCreateCatalogDefaults() {
      if (!this.newStoreBaseCurrency && this.addableCurrencies.length) {
        const rub = this.addableCurrencies.find(
          (c) => (c.code || "").toUpperCase() === "RUB",
        );
        this.newStoreBaseCurrency = (rub || this.addableCurrencies[0]).code;
      }
      if (!this.newStoreTimezone && this.timezoneOptions.length) {
        const moscow = this.timezoneOptions.find(
          (t) => t.value === "Europe/Moscow",
        );
        this.newStoreTimezone = (moscow || this.timezoneOptions[0]).value;
      }
    },

    resetCreateForm() {
      this.newStoreName = "";
      this.addAllUsers = true;
      this.createAttempted = false;
      this.newStoreBaseCurrency = "";
      this.newStoreTimezone = "";
      this.seedCreateCatalogDefaults();
    },

    async fetchStores() {
      this.isLoading = true;
      await this.getAllStores({
        onSuccess: (response) => {
          this.stores = response.data.value.items;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async fetchUsers() {
      await this.getAllUsers({
        onSuccess: (response) => {
          this.allUsers = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async handleCreateStore() {
      if (!this.canCreateStores) return;
      this.createAttempted = true;
      if (!this.canSubmitNewStore) return;

      this.isCreating = true;
      await this.createStore({
        name: this.newStoreName.trim(),
        addAllUsers: this.addAllUsers,
        timezone: this.newStoreTimezone || undefined,
        baseCurrency: this.newStoreBaseCurrency || undefined,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.storesPage.storeCreated"));
          this.showCreateDialog = false;
          this.fetchStores();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    async openEditDialog(store) {
      this.isEditDialogOpen = true;
      this.editingStore = null;
      this.editTab = "general";
      this.memberQuery = "";
      this.newCurrencyCode = "";
      this.newCurrencyRate = "";
      this.editForm = {
        name: "",
        memberIds: [],
        timezone: "",
        extendedPriceParameters: false,
      };
      this.storeCurrencies = [];

      await this.getStoreDetail({
        id: store.id,
        onSuccess: (response) => {
          const detail = response.data.value;
          this.editingStore = detail;
          this.editForm = {
            name: detail.name,
            memberIds: detail.members.map((member) => member.id),
            timezone: detail.timezone || this.timezoneOptions[0]?.value || "",
            extendedPriceParameters: Boolean(detail.extendedPriceParameters),
          };
          this.storeCurrencies = detail.currencies || [];
        },
        onError: (error) => {
          displaySonnerError(error);
          this.closeEditDialog();
        },
      });
    },

    closeEditDialog() {
      this.isEditDialogOpen = false;
      this.editingStore = null;
      this.editTab = "general";
      this.brandingFormDirty = false;
      this.brandingFormSaving = false;
      this.memberQuery = "";
      this.showDeleteDialog = false;
      this.deleteConfirmName = "";
      this.editForm = {
        name: "",
        memberIds: [],
        timezone: "",
        extendedPriceParameters: false,
      };
      this.storeCurrencies = [];
    },

    onEditDialogOpenChange(open) {
      if (!open && this.showDeleteDialog) {
        this.isEditDialogOpen = true;
        return;
      }
      if (!open) this.closeEditDialog();
      else this.isEditDialogOpen = true;
    },

    isCurrentStore(store) {
      return (
        this.me?.selectedStoreId != null &&
        store?.id != null &&
        this.me.selectedStoreId === store.id
      );
    },

    storeCaption(store) {
      const members = this.$t("pages.storesPage.membersCount", {
        count: store.membersCount || 0,
      });
      return this.isCurrentStore(store)
        ? `${members} · ${this.$t("pages.storesPage.current")}`
        : members;
    },

    toggleMember(userId, enabled) {
      const memberIds = new Set(this.editForm.memberIds);
      if (enabled) memberIds.add(userId);
      else memberIds.delete(userId);
      this.editForm.memberIds = Array.from(memberIds);
    },

    async saveStore() {
      if (!this.editingStore) return;
      const nameIssue = fieldLimitIssue(
        this.editForm.name,
        FIELD_LIMITS.storeName,
      );
      if (nameIssue === "empty") {
        displaySonnerError(this.$t("pages.storesPage.nameEmptyError"));
        return;
      }
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.storeName }),
        );
        return;
      }

      const data = {
        name: this.editForm.name.trim(),
        memberIds: this.editForm.memberIds,
        extendedPriceParameters: this.editForm.extendedPriceParameters,
      };
      // Omit timezone when empty / catalog unavailable - do not wipe or 400 on members-only saves.
      const timezone = (this.editForm.timezone || "").trim();
      if (timezone) data.timezone = timezone;

      this.isSaving = true;
      await this.updateStore({
        storeId: this.editingStore.id,
        data,
        onSuccess: async () => {
          displaySonnerSuccess(this.$t("pages.storesPage.storeUpdated"));
          this.closeEditDialog();
          await this.fetchStores();
          this.$store.commit("incrementAppKey");
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isSaving = false;
    },

    async saveDialog() {
      if (this.editTab === "branding") {
        await this.$refs.brandingForm?.save?.();
        return;
      }
      await this.saveStore();
    },

    async addCurrency() {
      if (
        !this.editingStore ||
        !this.newCurrencyCode ||
        !this.newCurrencyRate
      ) {
        displaySonnerError(this.$t("pages.storesPage.currencySelectError"));
        return;
      }
      const rate = Number(this.newCurrencyRate);
      if (!rate || rate <= 0) {
        displaySonnerError(this.$t("pages.storesPage.rateInvalidError"));
        return;
      }
      await this.addStoreCurrency({
        storeId: this.editingStore.id,
        data: { code: this.newCurrencyCode, rateToBase: rate },
        onSuccess: (r) => {
          this.storeCurrencies = r.data;
          this.newCurrencyCode = "";
          this.newCurrencyRate = "";
          displaySonnerSuccess(this.$t("pages.storesPage.currencyAdded"));
        },
        onError: displaySonnerError,
      });
    },

    async updateCurrencyRate(code, rateStr) {
      const rate = Number(rateStr);
      if (!rate || rate <= 0) return;
      await this.patchStoreCurrency({
        storeId: this.editingStore.id,
        code,
        data: { rateToBase: rate },
        onSuccess: (r) => {
          this.storeCurrencies = r.data;
        },
        onError: displaySonnerError,
      });
    },

    async removeCurrency(code) {
      await this.deleteStoreCurrency({
        storeId: this.editingStore.id,
        code,
        onSuccess: (r) => {
          this.storeCurrencies = r.data;
          displaySonnerSuccess(this.$t("pages.storesPage.currencyRemoved"));
        },
        onError: displaySonnerError,
      });
    },

    openDeleteDialog() {
      this.deleteConfirmName = "";
      this.showDeleteDialog = true;
    },

    async handleDeleteStore() {
      if (!this.deleteNameMatches || this.isDeleting || !this.editingStore)
        return;
      this.isDeleting = true;
      await this.deleteStore({
        storeId: this.editingStore.id,
        confirmationName: this.deleteConfirmName.trim(),
        onSuccess: async () => {
          displaySonnerSuccess(this.$t("pages.storeDetailPage.deleteSuccess"));
          this.showDeleteDialog = false;
          this.closeEditDialog();
          invalidateMeCache();
          try {
            await loadMe();
          } catch {
            /* ignore */
          }
          this.$store.commit("incrementAppKey");
          await this.fetchStores();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isDeleting = false;
    },

    async fetchMe() {
      try {
        this.me = await loadMe();
      } catch {
        this.me = null;
      }
    },
  },
  async mounted() {
    await this.fetchMe();
    this.fetchReferenceCatalogs();
    this.fetchStores();
    this.fetchUsers();
  },
};
</script>

<template>
  <div class="flex w-full max-w-xl flex-col pb-10">
    <div
      v-if="canCreateStores && !isLoading && stores.length > 0"
      class="mb-4 flex shrink-0 flex-wrap items-center justify-end gap-2"
    >
      <Button @click="showCreateDialog = true">
        <Plus class="size-4 mr-2" />
        {{ $t("pages.storesPage.createStore") }}
      </Button>
    </div>

    <PageSection
      :loading="isLoading"
      :empty="stores.length === 0"
      :empty-icon="Store"
      :empty-title="$t('pages.storesPage.emptyTitle')"
      :empty-description="$t('pages.storesPage.emptyDescription')"
    >
      <template v-if="canCreateStores" #empty-action>
        <Button @click="showCreateDialog = true">
          <Plus class="mr-2 size-4" />
          {{ $t("pages.storesPage.createStore") }}
        </Button>
      </template>

      <InsetGroup>
        <InsetGroupItem
          v-for="store in stores"
          :key="store.id"
          show-chevron
          :disabled="isSaving && editingStore?.id === store.id"
          @click="openEditDialog(store)"
        >
          <template #leading>
            <Store class="size-4" />
          </template>
          <p class="truncate">{{ store.name }}</p>
          <p class="text-muted-foreground mt-0.5 truncate text-xs font-normal">
            {{ storeCaption(store) }}
          </p>
        </InsetGroupItem>
      </InsetGroup>
    </PageSection>

    <Dialog v-if="canCreateStores" v-model:open="showCreateDialog">
      <DialogContent
        size="lg"
        :show-close="false"
        class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
      >
        <DialogHeader variant="bar">
          <div class="flex min-h-8 items-center gap-2">
            <DialogTitle class="min-w-0 flex-1 truncate">
              {{ $t("pages.storesPage.newStoreDialogTitle") }}
            </DialogTitle>
            <DialogHeaderClose />
          </div>
          <DialogDescription>
            {{ $t("pages.storesPage.newStoreDialogDescription") }}
          </DialogDescription>
        </DialogHeader>
        <form
          id="create-store-form"
          class="min-h-0 flex-1 overflow-y-auto px-6 py-5"
          @submit.prevent="handleCreateStore"
        >
          <FieldGroup class="gap-5">
            <SettingsFormField
              :label="$t('pages.storesPage.storeNameLabel')"
              label-for="new-store-name"
              :error="newStoreNameError"
              required
            >
              <Input
                id="new-store-name"
                v-model="newStoreName"
                :placeholder="$t('pages.storesPage.storeNamePlaceholder')"
                autocomplete="organization"
                :maxlength="FIELD_LIMITS.storeName"
                :disabled="isCreating"
              />
            </SettingsFormField>

            <template v-if="showCreateCatalogs">
              <SettingsFormField
                v-if="createCurrencyOptions.length"
                :label="$t('pages.storesPage.baseCurrencyLabel')"
              >
                <SearchableSelect
                  v-model="newStoreBaseCurrency"
                  :options="createCurrencyOptions"
                  :placeholder="$t('pages.storesPage.baseCurrencyPlaceholder')"
                  :disabled="isCreating"
                />
              </SettingsFormField>

              <SettingsFormField
                v-if="timezoneOptions.length"
                :label="$t('pages.storesPage.timezoneLabel')"
              >
                <SearchableSelect
                  v-model="newStoreTimezone"
                  :options="timezoneOptions"
                  :placeholder="$t('pages.storesPage.timezonePlaceholder')"
                  :disabled="isCreating"
                />
              </SettingsFormField>
            </template>
            <p v-else class="text-xs text-muted-foreground">
              {{ $t("pages.storesPage.catalogEmptyHint") }}
            </p>
          </FieldGroup>

          <InsetGroup class="mt-5">
            <InsetGroupItem
              :disabled="isCreating"
              :aria-pressed="addAllUsers"
              @click="addAllUsers = !addAllUsers"
            >
              <template #leading>
                <Users class="size-4" />
              </template>
              <p>{{ $t("pages.storesPage.addAllUsersLabel") }}</p>
              <p class="mt-0.5 text-xs font-normal text-muted-foreground">
                {{ $t("pages.storesPage.addAllUsersHint") }}
              </p>
              <template #trailing>
                <Switch
                  :model-value="addAllUsers"
                  :disabled="isCreating"
                  tabindex="-1"
                  class="pointer-events-none"
                />
              </template>
            </InsetGroupItem>
          </InsetGroup>
        </form>
        <DialogFooter variant="bar">
          <DialogClose as-child>
            <Button type="button" variant="outline">{{
              $t("common.cancel")
            }}</Button>
          </DialogClose>
          <Button
            type="submit"
            form="create-store-form"
            :disabled="isCreating || !canSubmitNewStore"
          >
            <Spinner v-if="isCreating" class="mr-2" />
            {{ $t("pages.storesPage.createStore") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog :open="isEditDialogOpen" @update:open="onEditDialogOpenChange">
      <DialogContent
        size="3xl"
        :show-close="false"
        class="flex h-[min(40rem,88vh)] max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
      >
        <DialogHeader variant="bar">
          <div class="flex min-h-8 items-center gap-2">
            <DialogTitle class="min-w-0 flex-1 truncate">
              {{ editingStore?.name || $t("pages.storesPage.editDialogTitle") }}
            </DialogTitle>
            <DialogHeaderClose />
          </div>
          <DialogDescription>
            {{ $t("pages.storesPage.editDialogDescription") }}
          </DialogDescription>
        </DialogHeader>

        <div
          v-if="!editingStore"
          class="flex min-h-0 flex-1 items-center justify-center"
        >
          <Spinner size="xl" class="text-muted-foreground" />
        </div>

        <Tabs v-else v-model="editTab" class="flex min-h-0 flex-1 flex-col">
          <div class="shrink-0 border-b px-6 py-3">
            <TabsList
              class="grid w-full grid-cols-2 sm:inline-flex sm:w-auto sm:grid-cols-none"
            >
              <TabsTrigger value="general">
                {{ $t("pages.storesPage.tabGeneral") }}
              </TabsTrigger>
              <TabsTrigger value="branding">
                {{ $t("pages.storesPage.tabBranding") }}
              </TabsTrigger>
              <TabsTrigger value="currencies">
                {{ $t("pages.storesPage.tabCurrencies") }}
              </TabsTrigger>
              <TabsTrigger value="members">
                {{ $t("pages.storesPage.tabMembers") }}
              </TabsTrigger>
            </TabsList>
          </div>

          <div class="relative min-h-0 flex-1">
            <TabsContent
              value="general"
              class="mt-0 absolute inset-0 space-y-6 overflow-y-auto px-6 py-5 outline-none"
            >
              <section>
                <InsetGroupLabel>{{
                  $t("pages.storesPage.identityTitle")
                }}</InsetGroupLabel>
                <p class="mb-2 px-1 text-xs text-muted-foreground">
                  {{ $t("pages.storesPage.identityHint") }}
                </p>
                <FieldGroup class="gap-3">
                  <SettingsFormField
                    :label="$t('common.name')"
                    label-for="store-name"
                  >
                    <Input
                      id="store-name"
                      v-model="editForm.name"
                      :maxlength="FIELD_LIMITS.storeName"
                      :disabled="isSaving"
                    />
                  </SettingsFormField>
                </FieldGroup>
              </section>

              <section>
                <InsetGroupLabel>{{
                  $t("pages.storesPage.regionalSettingsTitle")
                }}</InsetGroupLabel>
                <p class="mb-2 px-1 text-xs text-muted-foreground">
                  {{ $t("pages.storesPage.regionalHint") }}
                </p>
                <FieldGroup class="gap-3">
                  <SettingsFormField
                    :label="$t('pages.storesPage.timezoneLabel')"
                  >
                    <SearchableSelect
                      v-model="editForm.timezone"
                      :options="timezoneSelectOptions"
                      :placeholder="$t('pages.storesPage.timezonePlaceholder')"
                      :disabled="isSaving || !canChangeTimezone"
                    />
                    <template v-if="!canChangeTimezone" #description>
                      {{ $t("pages.storesPage.catalogEmptyHint") }}
                    </template>
                  </SettingsFormField>
                </FieldGroup>
              </section>

              <section>
                <InsetGroupLabel>{{
                  $t("pages.storesPage.pricesTitle")
                }}</InsetGroupLabel>
                <InsetGroup>
                  <InsetGroupItem
                    :as="isSaving ? 'div' : 'button'"
                    :disabled="isSaving"
                    :aria-pressed="editForm.extendedPriceParameters"
                    @click="
                      editForm.extendedPriceParameters =
                        !editForm.extendedPriceParameters
                    "
                  >
                    <template #leading>
                      <Coins class="size-4" />
                    </template>
                    <p>{{ $t("pages.storesPage.extendedPriceParamsLabel") }}</p>
                    <p class="mt-0.5 text-xs font-normal text-muted-foreground">
                      {{ $t("pages.storesPage.extendedPriceParamsHint") }}
                    </p>
                    <template #trailing>
                      <Switch
                        :model-value="editForm.extendedPriceParameters"
                        :disabled="isSaving"
                        tabindex="-1"
                        class="pointer-events-none"
                      />
                    </template>
                  </InsetGroupItem>
                </InsetGroup>
              </section>

              <section v-if="canDeleteStore">
                <InsetGroupLabel class="text-destructive">
                  {{ $t("pages.storeDetailPage.dangerZoneTitle") }}
                </InsetGroupLabel>
                <InsetGroup>
                  <InsetGroupItem
                    :disabled="isLastStore || isSaving"
                    @click="openDeleteDialog"
                  >
                    <template #leading>
                      <Trash2 class="size-4 text-destructive" />
                    </template>
                    <p class="text-destructive">
                      {{ $t("pages.storeDetailPage.deleteStoreButton") }}
                    </p>
                    <p class="mt-0.5 text-xs font-normal text-muted-foreground">
                      {{
                        isLastStore
                          ? $t("pages.storeDetailPage.deleteLastStoreError")
                          : $t("pages.storeDetailPage.dangerZoneDescription")
                      }}
                    </p>
                  </InsetGroupItem>
                </InsetGroup>
              </section>
            </TabsContent>

            <TabsContent
              value="branding"
              class="mt-0 absolute inset-0 overflow-y-auto px-6 py-5 outline-none"
            >
              <BrandingForm
                v-if="editTab === 'branding' && editingStore"
                ref="brandingForm"
                :store-id="editingStore.id"
                :apply-live="brandingApplyLive"
                compact
                @update:dirty="(v) => (brandingFormDirty = v)"
                @update:saving="(v) => (brandingFormSaving = v)"
              />
            </TabsContent>

            <TabsContent
              value="currencies"
              class="mt-0 absolute inset-0 space-y-5 overflow-y-auto px-6 py-5 outline-none"
            >
              <InsetGroupLabel>{{
                $t("pages.storesPage.currenciesTitle")
              }}</InsetGroupLabel>
              <p class="mb-2 px-1 text-xs text-muted-foreground">
                {{
                  $t("pages.storesPage.currenciesHint", {
                    base:
                      storeBaseCurrency ||
                      $t("pages.storesPage.baseCurrencyFallback"),
                  })
                }}
              </p>
              <p
                v-if="!(addableCurrencies || []).length"
                class="mb-2 px-1 text-xs text-muted-foreground"
              >
                {{ $t("pages.storesPage.catalogEmptyHint") }}
              </p>

              <InsetGroup>
                <InsetGroupItem as="div">
                  <p class="font-medium tabular-nums">
                    {{ storeBaseCurrency || "-" }}
                  </p>
                  <p class="mt-0.5 text-xs font-normal text-muted-foreground">
                    {{ $t("pages.storesPage.baseTag") }}
                  </p>
                  <template #trailing>
                    <span class="text-xs tabular-nums text-muted-foreground"
                      >1.000000</span
                    >
                  </template>
                </InsetGroupItem>
                <InsetGroupItem
                  v-for="cur in extraCurrencies"
                  :key="cur.code"
                  as="div"
                >
                  <p class="font-medium">{{ cur.code }}</p>
                  <p
                    class="mt-0.5 text-xs font-normal tabular-nums text-muted-foreground"
                  >
                    {{ formatCurrencyDate(cur.updatedAt) }}
                  </p>
                  <template #trailing>
                    <div class="flex items-center gap-1.5">
                      <Input
                        type="number"
                        step="0.000001"
                        min="0"
                        class="h-8 w-[7.5rem]"
                        :model-value="cur.rateToBase"
                        :disabled="isSaving"
                        :aria-label="
                          $t('pages.storesPage.columnRate', {
                            base:
                              storeBaseCurrency ||
                              $t('pages.storesPage.baseFallback'),
                          })
                        "
                        @change="
                          updateCurrencyRate(cur.code, $event.target.value)
                        "
                      />
                      <Button
                        variant="ghost"
                        size="icon"
                        reveal-on-row-hover
                        class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                        :disabled="isSaving"
                        @click="removeCurrency(cur.code)"
                      >
                        <Trash2 class="size-4" />
                        <span class="sr-only">{{ $t("common.delete") }}</span>
                      </Button>
                    </div>
                  </template>
                </InsetGroupItem>
              </InsetGroup>

              <div v-if="availableCurrencyOptions.length" class="pt-2">
                <InsetGroupLabel>{{
                  $t("pages.storesPage.addCurrencyTitle")
                }}</InsetGroupLabel>
                <FieldGroup
                  class="gap-3 sm:grid sm:grid-cols-[minmax(0,1fr)_9rem_auto] sm:items-end"
                >
                  <SettingsFormField
                    :label="$t('pages.storesPage.columnCurrency')"
                  >
                    <SearchableSelect
                      v-model="newCurrencyCode"
                      :options="availableCurrencySelectOptions"
                      :placeholder="
                        $t('pages.storesPage.addCurrencyPlaceholder')
                      "
                      :disabled="isSaving"
                    />
                  </SettingsFormField>
                  <SettingsFormField
                    :label="
                      $t('pages.storesPage.rateToPlaceholder', {
                        base:
                          storeBaseCurrency ||
                          $t('pages.storesPage.baseFallback'),
                      })
                    "
                  >
                    <Input
                      v-model="newCurrencyRate"
                      type="number"
                      step="0.01"
                      min="0"
                      class="w-full"
                      :disabled="isSaving"
                    />
                  </SettingsFormField>
                  <Button
                    type="button"
                    class="w-full sm:w-auto"
                    :disabled="isSaving"
                    @click="addCurrency"
                  >
                    <Plus class="mr-1.5 size-4" />
                    {{ $t("common.add") }}
                  </Button>
                </FieldGroup>
              </div>
            </TabsContent>

            <TabsContent
              value="members"
              class="mt-0 absolute inset-0 flex min-h-0 flex-col space-y-4 overflow-hidden px-6 py-5 outline-none"
            >
              <InsetGroupLabel>{{
                $t("pages.storesPage.membersTitle")
              }}</InsetGroupLabel>
              <p class="mb-2 px-1 text-xs text-muted-foreground">
                {{ $t("pages.storesPage.membersHint") }}
                ·
                {{
                  $t("pages.storesPage.membersSelected", {
                    count: selectedMembersCount,
                  })
                }}
              </p>

              <NavSearchField
                v-model="memberQuery"
                :placeholder="$t('pages.storesPage.memberSearchPlaceholder')"
                :disabled="isSaving"
              />

              <InsetGroup scrollable class="flex-1">
                <InsetGroupItem
                  v-if="filteredUsers.length === 0"
                  as="div"
                  class="hover:bg-transparent"
                >
                  <p class="font-normal text-muted-foreground">
                    {{ $t("pages.storesPage.membersEmptySearch") }}
                  </p>
                </InsetGroupItem>
                <InsetGroupItem
                  v-for="user in filteredUsers"
                  :key="user.id"
                  :disabled="isSaving"
                  :aria-pressed="editForm.memberIds.includes(user.id)"
                  @click="
                    toggleMember(user.id, !editForm.memberIds.includes(user.id))
                  "
                >
                  <p>{{ user.userName }}</p>
                  <template #trailing>
                    <Check
                      v-if="editForm.memberIds.includes(user.id)"
                      class="size-4 text-primary"
                    />
                  </template>
                </InsetGroupItem>
              </InsetGroup>
            </TabsContent>
          </div>
        </Tabs>

        <DialogFooter variant="bar" class="sm:justify-between">
          <p class="hidden text-xs text-muted-foreground sm:block">
            {{
              editTab === "branding"
                ? $t("pages.storesPage.brandingSaveHint")
                : $t("pages.storesPage.saveHint")
            }}
          </p>
          <div class="flex w-full gap-2 sm:w-auto">
            <Button
              variant="outline"
              class="flex-1 sm:flex-none"
              :disabled="isSaving || brandingFormSaving"
              @click="closeEditDialog"
            >
              {{ $t("common.cancel") }}
            </Button>
            <Button
              class="flex-1 sm:flex-none"
              :disabled="
                editTab === 'branding'
                  ? brandingFormSaving || !brandingFormDirty || !editingStore
                  : isSaving || !editingStore
              "
              @click="saveDialog"
            >
              <Spinner v-if="isSaving || brandingFormSaving" class="mr-2" />
              <Save v-else class="mr-2 size-4" />
              {{ $t("common.save") }}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="showDeleteDialog">
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>{{
            $t("pages.storeDetailPage.deleteDialogTitle")
          }}</DialogTitle>
          <DialogDescription>
            {{ $t("pages.storeDetailPage.deleteDialogDescription") }}
          </DialogDescription>
        </DialogHeader>
        <FieldGroup class="gap-4 py-2">
          <SettingsFormField
            :label="
              $t('pages.storeDetailPage.deleteConfirmLabel', {
                name: editingStore?.name,
              })
            "
            label-for="store-delete-confirm"
          >
            <Input
              id="store-delete-confirm"
              v-model="deleteConfirmName"
              :placeholder="
                $t('pages.storeDetailPage.deleteConfirmPlaceholder')
              "
              autocomplete="off"
              @keydown.enter="handleDeleteStore"
            />
          </SettingsFormField>
        </FieldGroup>
        <DialogFooter>
          <DialogClose as-child>
            <Button variant="outline" :disabled="isDeleting">{{
              $t("common.cancel")
            }}</Button>
          </DialogClose>
          <Button
            variant="destructive"
            :disabled="!deleteNameMatches || isDeleting"
            @click="handleDeleteStore"
          >
            <Spinner v-if="isDeleting" class="mr-2" />
            {{ $t("pages.storeDetailPage.deleteForever") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
