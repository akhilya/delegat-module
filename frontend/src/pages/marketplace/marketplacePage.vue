<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import {
  Plus,
  Store,
  Loader2,
  Package,
  Trash2,
  Settings,
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import Switch from "@/components/ui/switch/Switch.vue";
import { PageHeader } from "@/components/ui/page-header";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_ADMIN_PLUGINS } from "@/router";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Label,
    Badge,
    Plus,
    Store,
    Loader2,
    Package,
    Trash2,
    Settings,
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
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Switch,
    PageHeader,
    EmptyState,
  },
  data() {
    return {
      connections: [],
      adapters: [],
      isLoading: false,
      isCreating: false,
      isDeleting: false,
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
      this.isDeleting = true;
      await this.deleteConnection({
        connectionId,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("marketplace.marketplacePage.connectionDeleted"));
          this.fetchConnections();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isDeleting = false;
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
  },
  mounted() {
    this.fetchData();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      :title="$t('marketplace.marketplacePage.title')"
      :description="$t('marketplace.marketplacePage.description')"
    >
      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            {{ $t("marketplace.marketplacePage.addConnection") }}
          </Button>
        </DialogTrigger>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{{ $t("marketplace.marketplacePage.newConnectionTitle") }}</DialogTitle>
            <DialogDescription>
              {{ $t("marketplace.marketplacePage.newConnectionDescription") }}
            </DialogDescription>
          </DialogHeader>
          <div class="py-4 space-y-4">
            <!-- No plugins available -->
            <EmptyState
              v-if="adapters.length === 0"
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

            <template v-else>
              <div class="space-y-2">
                <Label>{{ $t("marketplace.marketplacePage.connectionNameLabel") }}</Label>
                <Input
                  v-model="newConnection.name"
                  :placeholder="$t('marketplace.marketplacePage.connectionNamePlaceholder')"
                />
              </div>

              <div class="space-y-2">
                <Label>{{ $t("marketplace.marketplacePage.adapterLabel") }}</Label>
                <Select v-model="selectedAdapter" @update:modelValue="onAdapterSelect">
                  <SelectTrigger>
                    <SelectValue :placeholder="$t('marketplace.marketplacePage.selectAdapterPlaceholder')" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem
                      v-for="adapter in adapters"
                      :key="adapter.key"
                      :value="adapter.key"
                    >
                      {{ adapter.displayName }}
                      <span v-if="adapter.version" class="text-muted-foreground">
                        · v{{ adapter.version }}
                      </span>
                      <span v-if="adapter.stability" class="text-muted-foreground">
                        · {{ adapter.stability }}
                      </span>
                    </SelectItem>
                  </SelectContent>
                </Select>
                <p v-if="selectedAdapter" class="text-sm text-muted-foreground">
                  {{ adapters.find((a) => a.key === selectedAdapter)?.description }}
                </p>
              </div>

              <template v-if="credentialFields?.length">
                <div
                  v-for="field in credentialFields"
                  :key="field.key"
                  class="space-y-2"
                >
                  <Label>
                    {{ field.displayName }}
                    <span v-if="field.isRequired" class="text-destructive">*</span>
                  </Label>
                  <Input
                    v-model="credentialValues[field.key]"
                    :type="field.isSecret ? 'password' : 'text'"
                    :placeholder="field.placeholder || field.description"
                    :required="field.isRequired"
                  />
                  <p v-if="field.description" class="text-xs text-muted-foreground">
                    {{ field.description }}
                  </p>
                </div>
              </template>

              <div class="flex items-center gap-2">
                <Switch :model-value="newConnection.isActive" @update:model-value="newConnection.isActive = $event" id="active" />
                <Label for="active">{{ $t("marketplace.marketplacePage.activeConnectionLabel") }}</Label>
              </div>
            </template>
          </div>
          <DialogFooter v-if="adapters.length > 0">
            <DialogClose as-child>
              <Button variant="outline" @click="resetForm">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button @click="handleCreateConnection" :disabled="isCreating">
              <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
              {{ $t("common.create") }}
            </Button>
          </DialogFooter>
          <DialogFooter v-else>
            <DialogClose as-child>
              <Button variant="outline">{{ $t("common.close") }}</Button>
            </DialogClose>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </PageHeader>

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
                          class="size-8"
                          @click.stop
                        >
                          <Trash2 class="size-4 text-destructive" />
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
                            @click="handleDeleteConnection(conn.id)"
                            :disabled="isDeleting"
                          >
                            <Loader2 v-if="isDeleting" class="size-4 mr-2 animate-spin" />
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
  </Page>
</template>
