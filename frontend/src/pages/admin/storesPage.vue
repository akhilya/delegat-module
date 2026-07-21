<script>

import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Plus,
  Store,
  Users,
  Loader2,
  Pencil,
  Save,
  Trash2,
  Globe2,
  Coins,
  Search,
  Clock3,
} from "lucide-vue-next";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import Separator from "@/components/ui/separator/Separator.vue";
import { formatDateTime } from "@/utils/dateTime";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { PageHeader } from "@/components/ui/page-header";

export default {
  components: {
    PageSection,
    Button,
    Input,
    Checkbox,
    Label,
    Badge,
    Plus,
    Store,
    Users,
    Loader2,
    Pencil,
    Save,
    Trash2,
    Globe2,
    Coins,
    Search,
    Clock3,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    PageHeader,
    Switch,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    Separator,
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
      isEditDialogOpen: false,
      newStoreName: "",
      addAllUsers: true,
      showCreateDialog: false,
      timezoneOptions: [],
      addableCurrencies: [],
      catalogReady: false,
      catalogLinked: false,
    };
  },
  computed: {
    availableCurrencyOptions() {
      const existing = new Set((this.storeCurrencies || []).map((c) => c.code));
      return (this.addableCurrencies || []).filter((c) => !existing.has(c.code));
    },
    extraCurrencies() {
      return (this.storeCurrencies || []).filter((c) => !c.isBase);
    },
    storeBaseCurrency() {
      return this.editingStore?.baseCurrency
        || this.storeCurrencies?.find((c) => c.isBase)?.code
        || null;
    },
    selectedMembersCount() {
      return this.editForm.memberIds.length;
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
      "getReferenceCurrencies",
      "getReferenceTimezones",
      "getReferenceStatus",
    ]),
    formatCurrencyDate(iso) {
      return formatDateTime(iso, this.editForm.timezone || "UTC").combined;
    },

    userInitials(userName) {
      const parts = String(userName || "").trim().split(/\s+/).filter(Boolean);
      if (!parts.length) return "?";
      if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
      return (parts[0][0] + parts[1][0]).toUpperCase();
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
          this.timezoneOptions = (Array.isArray(items) ? items : []).map((t) => ({
            value: t.value || t.id,
            label: t.label || t.id,
          }));
        },
        onError: () => {
          this.timezoneOptions = [];
        },
      });
      await this.getReferenceCurrencies({
        onSuccess: (response) => {
          const items = response.data?.value ?? response.data ?? [];
          this.addableCurrencies = (Array.isArray(items) ? items : []).map((c) => ({
            code: c.code,
            label: c.label || c.code,
          }));
        },
        onError: () => {
          this.addableCurrencies = [];
        },
      });
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
      if (!this.newStoreName.trim()) {
        displaySonnerError(this.$t("pages.storesPage.nameRequiredError"));
        return;
      }

      this.isCreating = true;
      await this.createStore({
        name: this.newStoreName.trim(),
        addAllUsers: this.addAllUsers,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("pages.storesPage.storeCreated"));
          this.newStoreName = "";
          this.addAllUsers = true;
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
      this.memberQuery = "";
      this.editForm = {
        name: "",
        memberIds: [],
        timezone: "",
        extendedPriceParameters: false,
      };
      this.storeCurrencies = [];
    },

    toggleMember(userId, enabled) {
      const memberIds = new Set(this.editForm.memberIds);
      if (enabled) memberIds.add(userId);
      else memberIds.delete(userId);
      this.editForm.memberIds = Array.from(memberIds);
    },

    async saveStore() {
      if (!this.editingStore) return;
      if (!this.editForm.name.trim()) {
        displaySonnerError(this.$t("pages.storesPage.nameEmptyError"));
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

    async addCurrency() {
      if (!this.editingStore || !this.newCurrencyCode || !this.newCurrencyRate) {
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
  },
  mounted() {
    this.fetchReferenceCatalogs();
    this.fetchStores();
    this.fetchUsers();
  },
};
</script>

<template>
  <div>
    <PageHeader :title="$t('routes.stores')" :description="$t('pages.storesPage.description')">
      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            {{ $t("pages.storesPage.createStore") }}
          </Button>
        </DialogTrigger>
        <DialogContent size="md">
          <DialogHeader>
            <DialogTitle>{{ $t("pages.storesPage.newStoreDialogTitle") }}</DialogTitle>
            <DialogDescription>
              {{ $t("pages.storesPage.newStoreDialogDescription") }}
            </DialogDescription>
          </DialogHeader>
          <div class="space-y-4 py-2">
            <div class="grid gap-2">
              <Label for="new-store-name">{{ $t("common.name") }}</Label>
              <Input
                id="new-store-name"
                v-model="newStoreName"
                :placeholder="$t('pages.storesPage.storeNamePlaceholder')"
                @keyup.enter="handleCreateStore"
              />
            </div>
            <label class="flex cursor-pointer items-start gap-3 rounded-lg border bg-muted/30 p-3 text-sm transition-colors hover:bg-muted/50">
              <Checkbox v-model="addAllUsers" class="mt-0.5" />
              <span class="grid gap-1">
                <span class="font-medium leading-none">{{ $t("pages.storesPage.addAllUsersLabel") }}</span>
                <span class="text-xs text-muted-foreground">
                  {{ $t("pages.storesPage.addAllUsersHint") }}
                </span>
              </span>
            </label>
          </div>
          <DialogFooter>
            <DialogClose as-child>
              <Button variant="outline">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button @click="handleCreateStore" :disabled="isCreating">
              <Loader2 v-if="isCreating" class="mr-2 size-4 animate-spin" />
              {{ $t("common.create") }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </PageHeader>

    <PageSection
      :loading="isLoading"
      :empty="stores.length === 0"
      :empty-icon="Store"
      :empty-title="$t('pages.storesPage.emptyTitle')"
      :empty-description="$t('pages.storesPage.emptyDescription')"
    >
      <template #empty-action>
        <Button @click="showCreateDialog = true">
          <Plus class="mr-2 size-4" />
          {{ $t("pages.storesPage.createStore") }}
        </Button>
      </template>

      <div class="overflow-hidden rounded-lg border">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead class="w-24">ID</TableHead>
                <TableHead>{{ $t("pages.storesPage.columnStore") }}</TableHead>
                <TableHead class="w-40">{{ $t("pages.storesPage.columnMembers") }}</TableHead>
                <TableHead class="w-20"></TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="store in stores" :key="store.id">
                <TableCell class="tabular-nums text-muted-foreground">{{ store.id }}</TableCell>
                <TableCell>
                  <div class="font-medium">{{ store.name }}</div>
                </TableCell>
                <TableCell>
                  <span class="inline-flex items-center gap-1.5 text-sm tabular-nums text-muted-foreground">
                    <Users class="size-4" />
                    {{ store.membersCount || 0 }}
                  </span>
                </TableCell>
                <TableCell class="text-right">
                  <Button variant="ghost" size="icon" @click="openEditDialog(store)">
                    <Pencil class="size-4" />
                    <span class="sr-only">{{ $t("pages.storesPage.editStoreSr") }}</span>
                  </Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>
    </PageSection>

    <Dialog v-model:open="isEditDialogOpen">
      <DialogContent size="3xl" class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0">
        <DialogHeader class="shrink-0 space-y-3 border-b px-6 py-5 text-left">
          <div class="flex items-start gap-3 pr-8">
            <div class="flex size-10 shrink-0 items-center justify-center rounded-lg border bg-muted/40">
              <Store class="size-5 text-muted-foreground" />
            </div>
            <div class="min-w-0 flex-1 space-y-1">
              <DialogTitle class="truncate text-lg">
                {{ editingStore?.name || $t("pages.storesPage.editDialogTitle") }}
              </DialogTitle>
              <DialogDescription>
                {{ $t("pages.storesPage.editDialogDescription") }}
              </DialogDescription>
              <div v-if="editingStore" class="flex flex-wrap items-center gap-2 pt-1">
                <Badge variant="secondary" class="font-normal tabular-nums">
                  ID {{ editingStore.id }}
                </Badge>
                <Badge variant="outline" class="gap-1 font-normal">
                  <Users class="size-3" />
                  {{ $t("pages.storesPage.membersSelected", { count: selectedMembersCount }) }}
                </Badge>
                <Badge v-if="storeBaseCurrency" variant="outline" class="gap-1 font-normal">
                  <Coins class="size-3" />
                  {{ storeBaseCurrency }}
                </Badge>
              </div>
            </div>
          </div>
        </DialogHeader>

        <div v-if="!editingStore" class="flex h-56 items-center justify-center">
          <Loader2 class="size-5 animate-spin text-muted-foreground" />
        </div>

        <Tabs
          v-else
          v-model="editTab"
          class="flex min-h-0 flex-1 flex-col"
        >
          <div class="shrink-0 border-b px-6 py-3">
            <TabsList class="grid w-full grid-cols-3 sm:inline-flex sm:w-auto">
              <TabsTrigger value="general" class="gap-1.5">
                <Globe2 class="size-3.5" />
                {{ $t("pages.storesPage.tabGeneral") }}
              </TabsTrigger>
              <TabsTrigger value="currencies" class="gap-1.5">
                <Coins class="size-3.5" />
                {{ $t("pages.storesPage.tabCurrencies") }}
              </TabsTrigger>
              <TabsTrigger value="members" class="gap-1.5">
                <Users class="size-3.5" />
                {{ $t("pages.storesPage.tabMembers") }}
              </TabsTrigger>
            </TabsList>
          </div>

          <div class="min-h-0 flex-1 overflow-y-auto">
            <div class="px-6 py-5">
              <TabsContent value="general" class="mt-0 space-y-6 outline-none">
                <section class="space-y-3">
                  <div class="space-y-1">
                    <h3 class="text-sm font-semibold tracking-tight">{{ $t("pages.storesPage.identityTitle") }}</h3>
                    <p class="text-xs text-muted-foreground">{{ $t("pages.storesPage.identityHint") }}</p>
                  </div>
                  <div class="grid gap-2">
                    <Label for="store-name">{{ $t("common.name") }}</Label>
                    <Input id="store-name" v-model="editForm.name" :disabled="isSaving" />
                  </div>
                </section>

                <Separator />

                <section class="space-y-3">
                  <div class="flex items-start gap-2">
                    <Clock3 class="mt-0.5 size-4 shrink-0 text-muted-foreground" />
                    <div class="space-y-1">
                      <h3 class="text-sm font-semibold tracking-tight">{{ $t("pages.storesPage.regionalSettingsTitle") }}</h3>
                      <p class="text-xs text-muted-foreground">{{ $t("pages.storesPage.regionalHint") }}</p>
                    </div>
                  </div>
                  <div class="grid gap-2">
                    <Label>{{ $t("pages.storesPage.timezoneLabel") }}</Label>
                    <Select
                      v-model="editForm.timezone"
                      :disabled="isSaving || !canChangeTimezone"
                    >
                      <SelectTrigger>
                        <SelectValue :placeholder="$t('pages.storesPage.timezoneLabel')" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem
                          v-for="tz in timezoneSelectOptions"
                          :key="tz.value"
                          :value="tz.value"
                        >
                          {{ tz.label }}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                    <p v-if="!canChangeTimezone" class="text-xs text-muted-foreground">
                      {{ $t("pages.storesPage.catalogEmptyHint") }}
                    </p>
                  </div>
                </section>

                <Separator />

                <section class="space-y-3">
                  <div class="space-y-1">
                    <h3 class="text-sm font-semibold tracking-tight">{{ $t("pages.storesPage.pricesTitle") }}</h3>
                    <p class="text-xs text-muted-foreground">{{ $t("pages.storesPage.extendedPriceParamsHint") }}</p>
                  </div>
                  <label class="flex cursor-pointer items-center justify-between gap-4 rounded-lg border bg-muted/20 px-4 py-3 transition-colors hover:bg-muted/35">
                    <span class="text-sm font-medium">{{ $t("pages.storesPage.extendedPriceParamsLabel") }}</span>
                    <Switch v-model="editForm.extendedPriceParameters" :disabled="isSaving" />
                  </label>
                </section>
              </TabsContent>

              <TabsContent value="currencies" class="mt-0 space-y-5 outline-none">
                <div class="space-y-1">
                  <h3 class="text-sm font-semibold tracking-tight">{{ $t("pages.storesPage.currenciesTitle") }}</h3>
                  <p class="text-xs text-muted-foreground">
                    {{ $t("pages.storesPage.currenciesHint", { base: storeBaseCurrency || $t("pages.storesPage.baseCurrencyFallback") }) }}
                  </p>
                  <p v-if="!(addableCurrencies || []).length" class="text-xs text-muted-foreground">
                    {{ $t("pages.storesPage.catalogEmptyHint") }}
                  </p>
                </div>

                <div class="overflow-hidden rounded-lg border">
                  <div class="flex items-center justify-between gap-3 border-b bg-muted/30 px-4 py-3">
                    <div class="flex items-center gap-2">
                      <span class="text-sm font-semibold tabular-nums">{{ storeBaseCurrency || "-" }}</span>
                      <Badge variant="secondary" class="font-normal">{{ $t("pages.storesPage.baseTag") }}</Badge>
                    </div>
                    <span class="text-xs tabular-nums text-muted-foreground">1.000000</span>
                  </div>

                  <div v-if="extraCurrencies.length === 0" class="px-4 py-8 text-center text-sm text-muted-foreground">
                    {{ $t("pages.storesPage.noExtraCurrencies") }}
                  </div>

                  <ul v-else class="divide-y">
                    <li
                      v-for="cur in extraCurrencies"
                      :key="cur.code"
                      class="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between"
                    >
                      <div class="min-w-0">
                        <div class="text-sm font-semibold">{{ cur.code }}</div>
                        <div class="text-xs tabular-nums text-muted-foreground">
                          {{ formatCurrencyDate(cur.updatedAt) }}
                        </div>
                      </div>
                      <div class="flex items-center gap-2">
                        <div class="grid gap-1">
                          <Label class="text-xs text-muted-foreground">
                            {{ $t("pages.storesPage.columnRate", { base: storeBaseCurrency || $t("pages.storesPage.baseFallback") }) }}
                          </Label>
                          <Input
                            type="number"
                            step="0.000001"
                            min="0"
                            class="h-9 w-[140px]"
                            :model-value="cur.rateToBase"
                            :disabled="isSaving"
                            @change="updateCurrencyRate(cur.code, $event.target.value)"
                          />
                        </div>
                        <Button
                          variant="ghost"
                          size="icon"
                          class="mt-5 shrink-0 text-muted-foreground hover:text-destructive"
                          :disabled="isSaving"
                          @click="removeCurrency(cur.code)"
                        >
                          <Trash2 class="size-4" />
                          <span class="sr-only">{{ $t("common.delete") }}</span>
                        </Button>
                      </div>
                    </li>
                  </ul>
                </div>

                <div v-if="availableCurrencyOptions.length" class="rounded-lg border border-dashed p-4">
                  <div class="mb-3 text-sm font-medium">{{ $t("pages.storesPage.addCurrencyTitle") }}</div>
                  <div class="flex flex-col gap-2 sm:flex-row sm:items-end">
                    <div class="grid flex-1 gap-1">
                      <Label class="text-xs text-muted-foreground">{{ $t("pages.storesPage.columnCurrency") }}</Label>
                      <Select v-model="newCurrencyCode" :disabled="isSaving">
                        <SelectTrigger>
                          <SelectValue :placeholder="$t('pages.storesPage.addCurrencyPlaceholder')" />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="c in availableCurrencyOptions" :key="c.code" :value="c.code">
                            {{ c.label }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                    <div class="grid gap-1">
                      <Label class="text-xs text-muted-foreground">
                        {{ $t("pages.storesPage.rateToPlaceholder", { base: storeBaseCurrency || $t("pages.storesPage.baseFallback") }) }}
                      </Label>
                      <Input
                        v-model="newCurrencyRate"
                        type="number"
                        step="0.01"
                        min="0"
                        class="w-full sm:w-[140px]"
                        :disabled="isSaving"
                      />
                    </div>
                    <Button variant="secondary" :disabled="isSaving" @click="addCurrency">
                      <Plus class="mr-1.5 size-4" />
                      {{ $t("common.add") }}
                    </Button>
                  </div>
                </div>
              </TabsContent>

              <TabsContent value="members" class="mt-0 space-y-4 outline-none">
                <div class="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
                  <div class="space-y-1">
                    <h3 class="text-sm font-semibold tracking-tight">{{ $t("pages.storesPage.membersTitle") }}</h3>
                    <p class="text-xs text-muted-foreground">{{ $t("pages.storesPage.membersHint") }}</p>
                  </div>
                  <Badge variant="secondary" class="w-fit font-normal tabular-nums">
                    {{ $t("pages.storesPage.membersSelected", { count: selectedMembersCount }) }}
                  </Badge>
                </div>

                <div class="relative">
                  <Search class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
                  <Input
                    v-model="memberQuery"
                    class="pl-9"
                    :placeholder="$t('pages.storesPage.memberSearchPlaceholder')"
                    :disabled="isSaving"
                  />
                </div>

                <div class="overflow-hidden rounded-lg border">
                  <div v-if="filteredUsers.length === 0" class="px-4 py-10 text-center text-sm text-muted-foreground">
                    {{ $t("pages.storesPage.membersEmptySearch") }}
                  </div>
                  <ul v-else class="max-h-[42vh] divide-y overflow-y-auto">
                    <li v-for="user in filteredUsers" :key="user.id">
                      <label
                        class="flex cursor-pointer items-center gap-3 px-4 py-3 transition-colors hover:bg-muted/40"
                        :class="editForm.memberIds.includes(user.id) ? 'bg-muted/25' : ''"
                      >
                        <div class="flex size-9 shrink-0 items-center justify-center rounded-full border bg-background text-xs font-semibold tracking-wide text-muted-foreground">
                          {{ userInitials(user.userName) }}
                        </div>
                        <div class="min-w-0 flex-1">
                          <div class="truncate text-sm font-medium">{{ user.userName }}</div>
                          <div class="text-xs tabular-nums text-muted-foreground">ID {{ user.id }}</div>
                        </div>
                        <Checkbox
                          :model-value="editForm.memberIds.includes(user.id)"
                          :disabled="isSaving"
                          @update:model-value="toggleMember(user.id, $event)"
                        />
                      </label>
                    </li>
                  </ul>
                </div>
              </TabsContent>
            </div>
          </div>
        </Tabs>

        <DialogFooter class="shrink-0 border-t bg-muted/20 px-6 py-4 sm:justify-between">
          <p class="hidden text-xs text-muted-foreground sm:block">
            {{ $t("pages.storesPage.saveHint") }}
          </p>
          <div class="flex w-full gap-2 sm:w-auto">
            <Button variant="outline" class="flex-1 sm:flex-none" :disabled="isSaving" @click="closeEditDialog">
              {{ $t("common.cancel") }}
            </Button>
            <Button class="flex-1 sm:flex-none" :disabled="isSaving || !editingStore" @click="saveStore">
              <Loader2 v-if="isSaving" class="mr-2 size-4 animate-spin" />
              <Save v-else class="mr-2 size-4" />
              {{ $t("common.save") }}
            </Button>
          </div>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
