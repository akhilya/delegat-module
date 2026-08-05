<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import Separator from "@/components/ui/separator/Separator.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import {
  Plus,
  Store,
  Package,
  Trash2,
  Settings,
  Check,
} from "lucide-vue-next";
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
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import Switch from "@/components/ui/switch/Switch.vue";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_ADMIN_PLUGINS } from "@/router";

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageSection,
    Button,
    Input,
    PasswordInput,
    Badge,
    SettingsFormField,
    FieldGroup,
    Separator,
    Plus,
    Store,
    Package,
    Trash2,
    Settings,
    Check,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogTrigger,
    Switch,
    EmptyState
  },
  data() {
    return {
      connections: [],
      adapters: [],
      isLoading: false,
      isCreating: false,
      deletingConnectionId: null,
      showCreateDialog: false,
      newConnection: {
        name: "",
        adapterKey: "",
        credentialsJson: "",
        isActive: true,
      },
      selectedAdapter: null,
      credentialValues: {},
    };
  },
  computed: {
    breadcrumbs() {
      return [{ label: this.$t("marketplace.marketplacePage.title") }];
    },
    selectedAdapterSchema() {
      if (!this.selectedAdapter) return null;
      const adapter = this.adapters.find((a) => a.key === this.selectedAdapter);
      return adapter?.credentialsSchema;
    },
    credentialFields() {
      // Handle both PascalCase (C# local) and camelCase (remote adapter) naming
      const schema = this.selectedAdapterSchema;
      if (!schema) return [];
      return schema.fields || schema.Fields || [];
    },
    selectedAdapterMeta() {
      if (!this.selectedAdapter) return null;
      return this.adapters.find((a) => a.key === this.selectedAdapter) || null;
    },
  },
  methods: {
    ...mapActions([
      "getConnections",
      "getAvailableAdapters",
      "createConnection",
      "deleteConnection",
    ]),

    async fetchData() {
      this.isLoading = true;
      await Promise.all([this.fetchConnections(), this.fetchAdapters()]);
      this.isLoading = false;
    },

    async fetchConnections() {
      await this.getConnections({
        onSuccess: (response) => {
          this.connections = extractResponseData(response, []);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    async fetchAdapters() {
      await this.getAvailableAdapters({
        onSuccess: (response) => {
          this.adapters = extractResponseData(response, []);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
    },

    onAdapterSelect(adapterKey) {
      this.selectedAdapter = adapterKey;
      this.newConnection.adapterKey = adapterKey;
      this.credentialValues = {};
    },

    buildCredentialsJson() {
      return JSON.stringify(this.credentialValues);
    },

    async handleCreateConnection() {
      if (!this.newConnection.name.trim()) {
        displaySonnerError(this.$t("marketplace.marketplacePage.enterConnectionName"));
        return;
      }
      if (!this.newConnection.adapterKey) {
        displaySonnerError(this.$t("marketplace.marketplacePage.selectAdapter"));
        return;
      }

      this.isCreating = true;
      await this.createConnection({
        data: {
          name: this.newConnection.name.trim(),
          adapterKey: this.newConnection.adapterKey,
          credentialsJson: this.buildCredentialsJson(),
          isActive: this.newConnection.isActive,
        },
        onSuccess: (response) => {
          displaySonnerSuccess(this.$t("marketplace.marketplacePage.connectionCreated"));
          this.resetForm();
          this.showCreateDialog = false;
          const data = extractResponseData(response, {});
          this.$router.push(`/adapter/${data.id}`);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    async handleDeleteConnection(connectionId) {
      if (this.deletingConnectionId != null) return;

      this.deletingConnectionId = connectionId;
      const previous = this.connections;
      // Remove immediately so a second click cannot race the in-flight DELETE.
      this.connections = this.connections.filter((c) => c.id !== connectionId);

      let failed = false;
      await this.deleteConnection({
        connectionId,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("marketplace.marketplacePage.connectionDeleted"));
        },
        onError: (error) => {
          failed = true;
          this.connections = previous;
          displaySonnerError(error);
        },
      });

      if (!failed) {
        await this.fetchConnections();
      }
      this.deletingConnectionId = null;
    },

    resetForm() {
      this.newConnection = {
        name: "",
        adapterKey: "",
        credentialsJson: "",
        isActive: true,
      };
      this.selectedAdapter = null;
      this.credentialValues = {};
    },

    openConnection(connectionId) {
      this.$router.push(`/adapter/${connectionId}`);
    },

    openPlugins() {
      this.$router.push(PAGE_ADMIN_PLUGINS);
    },

    getAdapterName(adapterKey) {
      const adapter = this.adapters.find((a) => a.key === adapterKey);
      return adapter?.displayName || adapterKey;
    },

    onCreateDialogOpenChange(open) {
      this.showCreateDialog = open;
      if (!open) this.resetForm();
    },

    adapterRowClass(adapterKey) {
      const selected = this.selectedAdapter === adapterKey;
      return [
        "w-full rounded-lg border bg-background p-3 text-left transition-colors outline-none",
        "focus-visible:ring-[3px] focus-visible:ring-ring/50",
        selected
          ? "border-primary bg-primary/5"
          : "border-input hover:bg-muted/40",
      ];
    },
  },
  mounted() {
    this.fetchData();
  },
};
</script>

<template>
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <div class="mb-4 flex flex-wrap items-center justify-end gap-2">
      <Dialog :open="showCreateDialog" @update:open="onCreateDialogOpenChange">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            {{ $t("marketplace.marketplacePage.addConnection") }}
          </Button>
        </DialogTrigger>
        <DialogContent
          size="lg"
          class="flex max-h-[85vh] flex-col gap-0 overflow-hidden p-0 sm:max-w-xl"
        >
          <DialogHeader variant="bar">
            <DialogTitle>{{ $t("marketplace.marketplacePage.newConnectionTitle") }}</DialogTitle>
            <DialogDescription>
              {{ $t("marketplace.marketplacePage.newConnectionDescription") }}
            </DialogDescription>
          </DialogHeader>

          <div class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
            <EmptyState
              v-if="adapters.length === 0"
              size="sm"
              :title="$t('marketplace.marketplacePage.noPluginsAvailableTitle')"
              :description="$t('marketplace.marketplacePage.noPluginsAvailableDescription')"
            >
              <template #icon>
                <Package />
              </template>
              <Button variant="outline" class="mt-4" @click="openPlugins">
                {{ $t("marketplace.marketplacePage.goToPlugins") }}
              </Button>
            </EmptyState>

            <FieldGroup v-else class="gap-5">
              <SettingsFormField
                :label="$t('marketplace.marketplacePage.connectionNameLabel')"
                label-for="new-connection-name"
              >
                <Input
                  id="new-connection-name"
                  v-model="newConnection.name"
                  :placeholder="$t('marketplace.marketplacePage.connectionNamePlaceholder')"
                  autocomplete="off"
                />
              </SettingsFormField>

              <SettingsFormField :label="$t('marketplace.marketplacePage.adapterLabel')">
                <div
                  class="space-y-2"
                  role="radiogroup"
                  :aria-label="$t('marketplace.marketplacePage.adapterLabel')"
                >
                  <button
                    v-for="adapter in adapters"
                    :key="adapter.key"
                    type="button"
                    role="radio"
                    :aria-checked="selectedAdapter === adapter.key"
                    :class="adapterRowClass(adapter.key)"
                    @click="onAdapterSelect(adapter.key)"
                  >
                    <div class="flex items-start gap-3">
                      <span
                        class="mt-0.5 flex size-4 shrink-0 items-center justify-center rounded-full border"
                        :class="
                          selectedAdapter === adapter.key
                            ? 'border-primary bg-primary text-primary-foreground'
                            : 'border-input bg-background'
                        "
                      >
                        <Check
                          v-if="selectedAdapter === adapter.key"
                          class="size-2.5"
                        />
                      </span>
                      <div class="min-w-0 flex-1 space-y-0.5">
                        <div class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
                          <span class="text-sm font-medium text-foreground">
                            {{ adapter.displayName }}
                          </span>
                          <span
                            v-if="adapter.version"
                            class="font-mono text-xs tabular-nums text-muted-foreground"
                          >
                            v{{ adapter.version }}
                          </span>
                          <Badge
                            v-if="adapter.stability"
                            variant="outline"
                            class="h-5 px-1.5 text-[10px] font-normal uppercase tracking-wide"
                          >
                            {{ adapter.stability }}
                          </Badge>
                        </div>
                        <p
                          v-if="adapter.description"
                          class="text-xs leading-relaxed text-muted-foreground"
                        >
                          {{ adapter.description }}
                        </p>
                      </div>
                    </div>
                  </button>
                </div>
                <template #description>
                  {{ $t("marketplace.marketplacePage.adapterPickHint") }}
                </template>
              </SettingsFormField>

              <template v-if="selectedAdapter && credentialFields.length > 0">
                <Separator />
                <div class="space-y-1">
                  <h3 class="text-sm font-medium tracking-tight">
                    {{ $t("marketplace.marketplacePage.credentialsSectionTitle") }}
                  </h3>
                  <p class="text-xs text-muted-foreground">
                    {{
                      $t("marketplace.marketplacePage.credentialsSectionHint", {
                        adapter: selectedAdapterMeta?.displayName || selectedAdapter,
                      })
                    }}
                  </p>
                </div>
                <FieldGroup class="gap-4">
                  <SettingsFormField
                    v-for="field in credentialFields"
                    :key="field.key"
                    :label="field.displayName || field.key"
                    :label-for="`new-cred-${field.key}`"
                    :required="field.isRequired"
                    :description="field.description || ''"
                  >
                    <PasswordInput
                      v-if="field.isSecret"
                      :id="`new-cred-${field.key}`"
                      v-model="credentialValues[field.key]"
                      :placeholder="field.placeholder || ''"
                      autocomplete="off"
                    />
                    <Input
                      v-else
                      :id="`new-cred-${field.key}`"
                      v-model="credentialValues[field.key]"
                      type="text"
                      :placeholder="field.placeholder || ''"
                      autocomplete="off"
                    />
                  </SettingsFormField>
                </FieldGroup>
              </template>

              <Separator />

              <SettingsFormField
                :label="$t('marketplace.marketplacePage.activeConnectionLabel')"
                label-for="new-connection-active"
                inline
                :description="$t('marketplace.marketplacePage.activeConnectionHint')"
              >
                <Switch
                  id="new-connection-active"
                  :model-value="newConnection.isActive"
                  @update:model-value="newConnection.isActive = $event"
                />
              </SettingsFormField>
            </FieldGroup>
          </div>

          <DialogFooter v-if="adapters.length > 0" variant="bar">
            <DialogClose as-child>
              <Button variant="outline">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button
              :disabled="isCreating || !newConnection.name.trim() || !selectedAdapter"
              @click="handleCreateConnection"
            >
              <Spinner v-if="isCreating" class="mr-2" />
              {{ $t("marketplace.marketplacePage.createConnection") }}
            </Button>
          </DialogFooter>
          <DialogFooter v-else variant="bar">
            <DialogClose as-child>
              <Button variant="outline">{{ $t("common.close") }}</Button>
            </DialogClose>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>

    <PageSection
      :loading="isLoading"
      :empty="connections.length === 0"
      :empty-icon="Store"
      :empty-title="$t('marketplace.marketplacePage.emptyTitle')"
      :empty-description="$t('marketplace.marketplacePage.emptyDescription')"
    >

      <div class="rounded-lg border overflow-hidden">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow class="hover:bg-transparent">
                <TableHead class="w-10" />
                <TableHead>{{ $t("common.name") }}</TableHead>
                <TableHead class="hidden sm:table-cell">{{ $t("marketplace.marketplacePage.adapterLabel") }}</TableHead>
                <TableHead class="w-32 text-right">{{ $t("marketplace.marketplacePage.mappingsColumn") }}</TableHead>
                <TableHead class="w-28 text-right">{{ $t("marketplace.marketplacePage.productsColumn") }}</TableHead>
                <TableHead class="w-24" />
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="conn in connections"
                :key="conn.id"
                class="group cursor-pointer"
                @click="openConnection(conn.id)"
              >
                <TableCell class="px-3">
                  <span
                    class="size-2 rounded-full block"
                    :class="conn.isActive ? 'bg-success' : 'bg-muted-foreground/40'"
                    :title="conn.isActive ? $t('marketplace.marketplacePage.active') : $t('marketplace.marketplacePage.inactive')"
                  />
                </TableCell>
                <TableCell class="font-medium max-w-0">
                  <span class="truncate block">{{ conn.name }}</span>
                  <span class="text-xs text-muted-foreground truncate block sm:hidden">
                    {{ getAdapterName(conn.adapterKey) }}
                  </span>
                </TableCell>
                <TableCell class="hidden sm:table-cell text-muted-foreground max-w-xs truncate">
                  {{ getAdapterName(conn.adapterKey) }}
                </TableCell>
                <TableCell class="text-right tabular-nums text-muted-foreground">
                  {{ conn.categoryMappingsCount || 0 }}
                </TableCell>
                <TableCell class="text-right tabular-nums text-muted-foreground">
                  {{ conn.productLinksCount || 0 }}
                </TableCell>
                <TableCell class="px-2">
                  <div class="flex items-center justify-end opacity-0 group-hover:opacity-100 transition-opacity">
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      :title="$t('common.settings')"
                      @click.stop="openConnection(conn.id)"
                    >
                      <Settings class="size-4" />
                    </Button>
                    <AlertDialog>
                      <AlertDialogTrigger as-child>
                        <Button
                          variant="ghost"
                          size="icon"
                          class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                          :disabled="deletingConnectionId === conn.id"
                          @click.stop
                        >
                          <Spinner v-if="deletingConnectionId === conn.id" />
                          <Trash2 v-else class="size-4" />
                        </Button>
                      </AlertDialogTrigger>
                      <AlertDialogContent>
                        <AlertDialogHeader>
                          <AlertDialogTitle>{{ $t("marketplace.marketplacePage.deleteConnectionTitle") }}</AlertDialogTitle>
                          <AlertDialogDescription>
                            {{ $t("marketplace.marketplacePage.deleteConnectionDescription", { name: conn.name }) }}
                          </AlertDialogDescription>
                        </AlertDialogHeader>
                        <AlertDialogFooter>
                          <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
                          <AlertDialogAction
                            variant="destructive"
                            @click="handleDeleteConnection(conn.id)"
                            :disabled="deletingConnectionId != null"
                          >
                            <Spinner v-if="deletingConnectionId === conn.id" class="mr-2" />
                            {{ $t("common.delete") }}
                          </AlertDialogAction>
                        </AlertDialogFooter>
                      </AlertDialogContent>
                    </AlertDialog>
                  </div>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>
    </PageSection>
  </AppPageChrome>
</template>
