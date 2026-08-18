<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_ADMIN_PLUGINS } from "@/router";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import ConnectionCreateForm from "@/components/marketplace/ConnectionCreateForm.vue";
import { getCachedMe } from "@/lib/meCache";
import { hasPermission, PERMS } from "@/lib/permissions";
import { FIELD_LIMITS, isNonEmptyWithinLimit } from "@/lib/fieldLimits";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData, apiErrorCode } from "@/store/sonnerHelper";
import {
  Plus,
  Store,
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

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageSection,
    Button,
    Plus,
    Store,
    Package,
    Trash2,
    Settings,
    AdapterIcon,
    ConnectionCreateForm,
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
    EmptyState,
  },
  data() {
    return {
      connections: [],
      adapters: [],
      isLoading: false,
      isCreating: false,
      deletingConnectionId: null,
      showCreateDialog: false,
      connectionQuotaReached: false,
    };
  },
  computed: {
    breadcrumbs() {
      return [{ label: this.$t("marketplace.marketplacePage.title") }];
    },
    canOpenPlugins() {
      const me = getCachedMe();
      return me?.pluginsEnabled === true && hasPermission(me, PERMS.PluginsManage);
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
          const data = extractResponseData(response, []);
          this.connections = Array.isArray(data) ? data : [];
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

    async handleCreateConnection() {
      const form = this.$refs.createForm;
      const data = form?.payload?.();
      if (!data?.name) {
        displaySonnerError(this.$t("marketplace.marketplacePage.enterConnectionName"));
        return;
      }
      if (!isNonEmptyWithinLimit(data.name, FIELD_LIMITS.connectionName)) {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.connectionName }),
        );
        return;
      }
      if (!data.adapterKey) {
        displaySonnerError(this.$t("marketplace.marketplacePage.selectAdapter"));
        return;
      }
      if (!form.canSubmit) {
        displaySonnerError(this.$t("marketplace.marketplacePage.enterCredentials"));
        return;
      }

      this.isCreating = true;
      await this.createConnection({
        data,
        onSuccess: (response) => {
          displaySonnerSuccess(this.$t("marketplace.marketplacePage.connectionCreated"));
          this.resetForm();
          this.showCreateDialog = false;
          const created = extractResponseData(response, {});
          this.$router.push(`/adapter/${created.id}`);
        },
        onError: (error) => {
          if (apiErrorCode(error) === "quota_connections_reached") {
            this.connectionQuotaReached = true;
            this.showCreateDialog = false;
          }
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    async handleDeleteConnection(connectionId) {
      if (this.deletingConnectionId != null) return;

      this.deletingConnectionId = connectionId;
      const previous = this.connections;
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
        this.connectionQuotaReached = false;
        await this.fetchConnections();
      }
      this.deletingConnectionId = null;
    },

    resetForm() {
      this.$refs.createForm?.reset?.();
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
              <Button
                v-if="canOpenPlugins"
                variant="outline"
                class="mt-4"
                @click="openPlugins"
              >
                {{ $t("marketplace.marketplacePage.goToPlugins") }}
              </Button>
            </EmptyState>

            <EmptyState
              v-else-if="connectionQuotaReached"
              size="sm"
              :title="$t('marketplace.marketplacePage.connectionQuotaTitle')"
              :description="$t('marketplace.marketplacePage.connectionQuotaDescription')"
            >
              <template #icon>
                <Store />
              </template>
            </EmptyState>

            <form
              v-else
              id="new-connection-form"
              class="contents"
              @submit.prevent="handleCreateConnection"
            >
              <ConnectionCreateForm
                ref="createForm"
                :adapters="adapters"
                :disabled="isCreating"
                id-prefix="new-conn"
              />
            </form>
          </div>

          <DialogFooter v-if="adapters.length > 0 && !connectionQuotaReached" variant="bar">
            <DialogClose as-child>
              <Button type="button" variant="outline">{{ $t("common.cancel") }}</Button>
            </DialogClose>
            <Button
              type="submit"
              form="new-connection-form"
              :disabled="isCreating"
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
                <TableHead class="hidden w-40 sm:table-cell">{{ $t("marketplace.marketplacePage.adapterLabel") }}</TableHead>
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
                  <div class="flex items-center gap-2 min-w-0">
                    <AdapterIcon :adapter-key="conn.adapterKey" class="size-4" />
                    <span class="truncate block">{{ conn.name }}</span>
                  </div>
                  <span class="text-xs text-muted-foreground truncate block sm:hidden pl-6">
                    {{ getAdapterName(conn.adapterKey) }}
                  </span>
                </TableCell>
                <TableCell class="hidden sm:table-cell text-muted-foreground max-w-xs truncate">
                  <div class="flex items-center gap-2 min-w-0">
                    <AdapterIcon :adapter-key="conn.adapterKey" class="size-4" />
                    <span class="truncate">{{ getAdapterName(conn.adapterKey) }}</span>
                  </div>
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
