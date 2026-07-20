<script>

import PageSection from "@/components/pageSection.vue";


import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, Store, Users, Loader2, Pencil, Save, ShieldCheck, Trash2 } from "lucide-vue-next";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { STORE_TIMEZONES, DEFAULT_TIMEZONE } from "@/lib/timezones";
import { ADDABLE_CURRENCIES } from "@/lib/currency";
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
    Plus,
    Store,
    Users,
    Loader2,
    Pencil,
    Save,
    ShieldCheck,
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
    Trash2,
  },
  data() {
    return {
      stores: [],
      allUsers: [],
      editingStore: null,
      editForm: {
        name: "",
        memberIds: [],
        storeAdminIds: [],
        timezone: DEFAULT_TIMEZONE,
        extendedPriceParameters: false,
      },
      storeCurrencies: [],
      newCurrencyCode: "",
      newCurrencyRate: "",
      isLoading: false,
      isCreating: false,
      isSaving: false,
      isEditDialogOpen: false,
      newStoreName: "",
      addAllUsers: true,
      showCreateDialog: false,
      timezoneOptions: STORE_TIMEZONES,
    };
  },
  computed: {
    availableCurrencyOptions() {
      const existing = new Set((this.storeCurrencies || []).map((c) => c.code));
      return ADDABLE_CURRENCIES.filter((c) => !existing.has(c.code));
    },
    extraCurrencies() {
      return (this.storeCurrencies || []).filter((c) => !c.isBase);
    },
    storeBaseCurrency() {
      return this.editingStore?.baseCurrency
        || this.storeCurrencies?.find((c) => c.isBase)?.code
        || null;
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
    ]),
    formatCurrencyDate(iso) {
      return formatDateTime(iso, this.editForm.timezone).combined;
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
        onSuccess: (response) => {
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
      this.editForm = {
        name: "",
        memberIds: [],
        storeAdminIds: [],
        timezone: DEFAULT_TIMEZONE,
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
            storeAdminIds: detail.members.filter((member) => member.isStoreAdmin).map((member) => member.id),
            timezone: detail.timezone || DEFAULT_TIMEZONE,
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
      this.editForm = {
        name: "",
        memberIds: [],
        storeAdminIds: [],
        timezone: DEFAULT_TIMEZONE,
        extendedPriceParameters: false,
      };
      this.storeCurrencies = [];
    },

    toggleMember(userId, enabled) {
      const memberIds = new Set(this.editForm.memberIds);
      const adminIds = new Set(this.editForm.storeAdminIds);

      if (enabled) {
        memberIds.add(userId);
      } else {
        memberIds.delete(userId);
        adminIds.delete(userId);
      }

      this.editForm.memberIds = Array.from(memberIds);
      this.editForm.storeAdminIds = Array.from(adminIds);
    },

    toggleStoreAdmin(userId, enabled) {
      const memberIds = new Set(this.editForm.memberIds);
      const adminIds = new Set(this.editForm.storeAdminIds);

      if (enabled) {
        memberIds.add(userId);
        adminIds.add(userId);
      } else {
        adminIds.delete(userId);
      }

      this.editForm.memberIds = Array.from(memberIds);
      this.editForm.storeAdminIds = Array.from(adminIds);
    },

    async saveStore() {
      if (!this.editingStore) return;
      if (!this.editForm.name.trim()) {
        displaySonnerError(this.$t("pages.storesPage.nameEmptyError"));
        return;
      }

      this.isSaving = true;
      await this.updateStore({
        storeId: this.editingStore.id,
        data: {
          name: this.editForm.name.trim(),
          memberIds: this.editForm.memberIds,
          storeAdminIds: this.editForm.storeAdminIds,
          timezone: this.editForm.timezone,
          extendedPriceParameters: this.editForm.extendedPriceParameters,
        },
        onSuccess: async () => {
          displaySonnerSuccess(this.$t("pages.storesPage.storeUpdated"));
          this.closeEditDialog();
          await this.fetchStores();
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
            <div class="py-4 space-y-4">
              <Input
                v-model="newStoreName"
                :placeholder="$t('pages.storesPage.storeNamePlaceholder')"
                @keyup.enter="handleCreateStore"
              />
              <label class="flex items-start gap-3 rounded-lg border bg-muted/20 p-3 text-sm cursor-pointer">
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
                <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
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
          <Plus class="size-4 mr-2" />
          {{ $t("pages.storesPage.createStore") }}
        </Button>
      </template>

      <div class="rounded-lg border overflow-hidden">
      <div class="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-24">ID</TableHead>
              <TableHead>{{ $t("pages.storesPage.columnStore") }}</TableHead>
              <TableHead class="w-40">{{ $t("pages.storesPage.columnMembers") }}</TableHead>
              <TableHead class="w-40">{{ $t("pages.storesPage.columnStoreAdmins") }}</TableHead>
              <TableHead class="w-20"></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="store in stores" :key="store.id">
              <TableCell class="text-muted-foreground tabular-nums">{{ store.id }}</TableCell>
              <TableCell>
                <div class="font-medium">{{ store.name }}</div>
              </TableCell>
              <TableCell>
                <span class="inline-flex items-center gap-1.5 text-sm text-muted-foreground tabular-nums">
                  <Users class="size-4" />
                  {{ store.membersCount || 0 }}
                </span>
              </TableCell>
              <TableCell>
                <span class="inline-flex items-center gap-1.5 text-sm text-muted-foreground tabular-nums">
                  <ShieldCheck class="size-4" />
                  {{ store.storeAdminsCount || 0 }}
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
      <DialogContent size="2xl">
        <DialogHeader>
          <DialogTitle>{{ $t("pages.storesPage.editDialogTitle") }}</DialogTitle>
          <DialogDescription>
            {{ $t("pages.storesPage.editDialogDescription") }}
          </DialogDescription>
        </DialogHeader>

        <div v-if="!editingStore" class="flex h-40 items-center justify-center">
          <Loader2 class="size-5 animate-spin text-muted-foreground" />
        </div>

        <div v-else class="grid gap-5 max-h-[70vh] overflow-y-auto pr-1">
          <div class="grid gap-2">
            <label class="text-sm font-medium" for="store-name">{{ $t("common.name") }}</label>
            <Input id="store-name" v-model="editForm.name" :disabled="isSaving" />
          </div>

          <div class="grid gap-3 rounded-lg border p-4">
            <h3 class="text-sm font-semibold">{{ $t("pages.storesPage.regionalSettingsTitle") }}</h3>
            <div class="grid gap-2">
              <label class="text-sm text-muted-foreground">{{ $t("pages.storesPage.timezoneLabel") }}</label>
              <Select v-model="editForm.timezone" :disabled="isSaving">
                <SelectTrigger>
                  <SelectValue :placeholder="$t('pages.storesPage.timezoneLabel')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="tz in timezoneOptions" :key="tz.value" :value="tz.value">
                    {{ tz.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
            </div>
          </div>

          <div class="grid gap-3 rounded-lg border p-4">
            <h3 class="text-sm font-semibold">{{ $t("pages.storesPage.pricesTitle") }}</h3>
            <label class="flex items-center justify-between gap-3 cursor-pointer">
              <span class="text-sm">{{ $t("pages.storesPage.extendedPriceParamsLabel") }}</span>
              <Switch v-model="editForm.extendedPriceParameters" :disabled="isSaving" />
            </label>
            <p class="text-xs text-muted-foreground">
              {{ $t("pages.storesPage.extendedPriceParamsHint") }}
            </p>
          </div>

          <div class="grid gap-3 rounded-lg border p-4">
            <h3 class="text-sm font-semibold">{{ $t("pages.storesPage.currenciesTitle") }}</h3>
            <p class="text-xs text-muted-foreground">
              {{ $t("pages.storesPage.currenciesHint", { base: storeBaseCurrency || $t("pages.storesPage.baseCurrencyFallback") }) }}
            </p>
            <div class="overflow-hidden rounded-md border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>{{ $t("pages.storesPage.columnCurrency") }}</TableHead>
                    <TableHead>{{ $t("pages.storesPage.columnRate", { base: storeBaseCurrency || $t("pages.storesPage.baseFallback") }) }}</TableHead>
                    <TableHead>{{ $t("pages.storesPage.columnUpdated") }}</TableHead>
                    <TableHead class="w-12" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow>
                    <TableCell class="font-medium">{{ storeBaseCurrency || "—" }}</TableCell>
                    <TableCell class="text-muted-foreground">1.000000</TableCell>
                    <TableCell class="text-muted-foreground text-xs">{{ $t("pages.storesPage.baseTag") }}</TableCell>
                    <TableCell />
                  </TableRow>
                  <TableRow v-for="cur in extraCurrencies" :key="cur.code">
                    <TableCell class="font-medium">{{ cur.code }}</TableCell>
                    <TableCell>
                      <Input
                        type="number"
                        step="0.000001"
                        min="0"
                        class="h-8 max-w-[140px]"
                        :model-value="cur.rateToBase"
                        :disabled="isSaving"
                        @change="updateCurrencyRate(cur.code, $event.target.value)"
                      />
                    </TableCell>
                    <TableCell class="text-xs text-muted-foreground tabular-nums">
                      {{ formatCurrencyDate(cur.updatedAt) }}
                    </TableCell>
                    <TableCell>
                      <Button variant="ghost" size="icon" class="h-8 w-8" :disabled="isSaving" @click="removeCurrency(cur.code)">
                        <Trash2 class="size-4" />
                      </Button>
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>
            <div v-if="availableCurrencyOptions.length" class="flex flex-wrap items-end gap-2">
              <Select v-model="newCurrencyCode" :disabled="isSaving">
                <SelectTrigger class="w-[180px]">
                  <SelectValue :placeholder="$t('pages.storesPage.addCurrencyPlaceholder')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="c in availableCurrencyOptions" :key="c.code" :value="c.code">
                    {{ c.label }}
                  </SelectItem>
                </SelectContent>
              </Select>
              <Input
                v-model="newCurrencyRate"
                type="number"
                step="0.01"
                min="0"
                :placeholder="$t('pages.storesPage.rateToPlaceholder', { base: storeBaseCurrency || $t('pages.storesPage.baseFallback') })"
                class="w-[140px]"
                :disabled="isSaving"
              />
              <Button variant="outline" size="sm" :disabled="isSaving" @click="addCurrency">{{ $t("common.add") }}</Button>
            </div>
          </div>

          <div class="overflow-hidden rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>{{ $t("nav.user") }}</TableHead>
                  <TableHead class="w-32">{{ $t("pages.storesPage.columnMemberSingular") }}</TableHead>
                  <TableHead class="w-40">{{ $t("pages.storesPage.columnStoreAdminSingular") }}</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="user in allUsers" :key="user.id">
                  <TableCell>
                    <div class="font-medium">{{ user.userName }}</div>
                    <div class="text-xs text-muted-foreground">ID {{ user.id }}</div>
                  </TableCell>
                  <TableCell>
                    <Checkbox
                      :model-value="editForm.memberIds.includes(user.id)"
                      :disabled="isSaving"
                      @update:model-value="toggleMember(user.id, $event)"
                    />
                  </TableCell>
                  <TableCell>
                    <Checkbox
                      :model-value="editForm.storeAdminIds.includes(user.id)"
                      :disabled="isSaving"
                      @update:model-value="toggleStoreAdmin(user.id, $event)"
                    />
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" :disabled="isSaving" @click="closeEditDialog">{{ $t("common.cancel") }}</Button>
          <Button :disabled="isSaving || !editingStore" @click="saveStore">
            <Loader2 v-if="isSaving" class="mr-2 size-4 animate-spin" />
            <Save v-else class="mr-2 size-4" />
            {{ $t("common.save") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
