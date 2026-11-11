<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import { EmptyState } from "@/components/ui/empty-state";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import { PAGE_ADMIN_PLUGINS, PAGE_CATALOG_CONNECT } from "@/router";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import AdapterProductDirectionOptions from "@/components/marketplace/AdapterProductDirectionOptions.vue";
import ExperimentalBadge from "@/components/marketplace/ExperimentalBadge.vue";
import { isExperimentalConnection } from "@/lib/adapterStage";
import {
  adapterCanImportAll,
  adapterCanLink,
  adapterHasCapability,
} from "@/lib/adapterCapabilities";
import ConnectionCreateForm from "@/components/marketplace/ConnectionCreateForm.vue";
import { getCachedMe } from "@/lib/meCache";
import { hasPermission, PERMS } from "@/lib/permissions";
import { handleRadioGroupKeydown } from "@/lib/radioGroup";
import { FIELD_LIMITS, isNonEmptyWithinLimit } from "@/lib/fieldLimits";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
  apiErrorCode,
} from "@/store/sonnerHelper";
import { Plus, Store, Package, Trash2 } from "lucide-vue-next";
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
    AdapterIcon,
    ExperimentalBadge,
    ConnectionCreateForm,
    InsetGroup,
    InsetGroupItem,
    AdapterProductDirectionOptions,
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
      createDialogStep: 1,
      selectedAdapterKey: "",
      createdConnection: null,
      connectionQuotaReached: false,
    };
  },
  computed: {
    canOpenPlugins() {
      const me = getCachedMe();
      return (
        me?.pluginsEnabled === true && hasPermission(me, PERMS.PluginsManage)
      );
    },
  },
  methods: {
    ...mapActions([
      "getConnections",
      "getAvailableAdapters",
      "createConnection",
      "deleteConnection",
    ]),
    handleRadioGroupKeydown,

    canAdapterImportAll(adapter) {
      return adapterCanImportAll(adapter);
    },

    canAdapterPublish(adapter) {
      return adapterHasCapability(adapter, "ExportProduct");
    },

    canAdapterSync(adapter) {
      return adapterCanLink(adapter);
    },

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

      this.isCreating = true;
      await this.createConnection({
        data,
        onSuccess: (response) => {
          displaySonnerSuccess(
            this.$t("marketplace.marketplacePage.connectionCreated"),
          );
          const created = extractResponseData(response, {});
          this.createdConnection = {
            ...created,
            name: data.name,
            adapterKey: data.adapterKey,
          };
          this.createDialogStep = 3;
          this.fetchConnections();
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
          displaySonnerSuccess(
            this.$t("marketplace.marketplacePage.connectionDeleted"),
          );
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

    resetCreateFlow() {
      this.resetForm();
      this.createDialogStep = 1;
      this.selectedAdapterKey = "";
      this.createdConnection = null;
    },

    onCreateDialogOpenChange(open) {
      this.showCreateDialog = open;
      if (!open) {
        const createdConnectionId = this.createdConnection?.id;
        this.resetCreateFlow();
        if (createdConnectionId) {
          this.$router.push(`/adapter/${createdConnectionId}`);
        }
      }
    },

    selectAdapter(adapterKey) {
      this.selectedAdapterKey = adapterKey;
    },

    advanceCreateStep() {
      if (this.createDialogStep === 1 && this.selectedAdapterKey) {
        this.createDialogStep = 2;
      }
    },

    backCreateStep() {
      if (this.createDialogStep === 2) {
        this.createDialogStep = 1;
      }
    },

    continueWithConnection(action) {
      const connectionId = this.createdConnection?.id;
      if (!connectionId) return;
      const path = `/adapter/${connectionId}`;
      this.showCreateDialog = false;
      this.resetCreateFlow();
      if (action === "publish") {
        this.$router.push(`${path}?startPublish=1`);
      } else if (action === "import") {
        this.$router.push(`${path}?tab=products&view=external`);
      } else if (action === "sync") {
        this.$router.push({
          path: PAGE_CATALOG_CONNECT,
          query: { matchConnectionId: String(connectionId) },
        });
      } else {
        this.$router.push(path);
      }
    },

    openPlugins() {
      this.$router.push(PAGE_ADMIN_PLUGINS);
    },

    getAdapterName(adapterKey) {
      const adapter = this.adapters.find((a) => a.key === adapterKey);
      return adapter?.displayName || adapterKey;
    },

    isExperimentalConnection(connection) {
      return isExperimentalConnection(connection, this.adapters);
    },

    connectionRowSubtitle(connection) {
      return this.$t("marketplace.marketplacePage.connectionRowSubtitle", {
        adapter: this.getAdapterName(connection.adapterKey),
        mappings: connection.categoryMappingsCount || 0,
        products: connection.productLinksCount || 0,
        status: connection.isActive
          ? this.$t("marketplace.marketplacePage.active")
          : this.$t("marketplace.marketplacePage.inactive"),
      });
    },
  },
  mounted() {
    this.fetchData();
  },
};
</script>

<template>
  <AppPageChrome>
    <div class="flex w-full max-w-xl flex-col">
      <p class="mb-4 text-sm text-muted-foreground">
        {{ $t("marketplace.marketplacePage.description") }}
      </p>
      <div class="mb-6 flex flex-wrap items-center justify-start gap-2">
        <Dialog
          :open="showCreateDialog"
          @update:open="onCreateDialogOpenChange"
        >
          <DialogTrigger as-child>
            <Button>
              <Plus class="size-4 mr-2" />
              {{ $t("marketplace.marketplacePage.addConnection") }}
            </Button>
          </DialogTrigger>
          <DialogContent
            size="2xl"
            class="flex max-h-[min(88vh,48rem)] flex-col gap-0 overflow-hidden p-0"
          >
            <DialogHeader variant="bar" class="pr-12">
              <DialogTitle>
                {{
                  createDialogStep === 3
                    ? $t("marketplace.marketplacePage.connectionSetupTitle")
                    : $t("marketplace.marketplacePage.newConnectionTitle")
                }}
              </DialogTitle>
              <DialogDescription class="max-w-lg leading-relaxed">
                {{
                  createDialogStep === 1
                    ? $t("marketplace.marketplacePage.chooseAdapterStep")
                    : createDialogStep === 2
                      ? $t(
                          "marketplace.marketplacePage.connectionCredentialsStep",
                        )
                      : $t(
                          "marketplace.marketplacePage.connectionSetupDescription",
                        )
                }}
              </DialogDescription>
            </DialogHeader>

            <div class="min-h-0 flex-1 overflow-y-auto px-6 py-6">
              <EmptyState
                v-if="adapters.length === 0"
                size="sm"
                :title="
                  $t('marketplace.marketplacePage.noPluginsAvailableTitle')
                "
                :description="
                  $t(
                    'marketplace.marketplacePage.noPluginsAvailableDescription',
                  )
                "
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
                :description="
                  $t('marketplace.marketplacePage.connectionQuotaDescription')
                "
              >
                <template #icon>
                  <Store />
                </template>
              </EmptyState>

              <template v-else-if="!connectionQuotaReached">
                <Transition
                  mode="out-in"
                  enter-active-class="transition duration-150 ease-out motion-reduce:transition-none"
                  enter-from-class="translate-y-1 opacity-0 motion-reduce:translate-y-0"
                  enter-to-class="translate-y-0 opacity-100"
                  leave-active-class="transition duration-100 ease-in motion-reduce:transition-none"
                  leave-from-class="translate-y-0 opacity-100"
                  leave-to-class="-translate-y-1 opacity-0 motion-reduce:translate-y-0"
                >
                  <div :key="createDialogStep">
                    <section v-if="createDialogStep === 1">
                      <InsetGroup
                        role="radiogroup"
                        :aria-label="
                          $t('marketplace.marketplacePage.adapterLabel')
                        "
                      >
                        <InsetGroupItem
                          v-for="(adapter, index) in adapters"
                          :key="adapter.key"
                          as="div"
                          role="radio"
                          :aria-checked="selectedAdapterKey === adapter.key"
                          :tabindex="
                            selectedAdapterKey === adapter.key || index === 0
                              ? 0
                              : -1
                          "
                          :selected="selectedAdapterKey === adapter.key"
                          @keydown="handleRadioGroupKeydown"
                          @click="selectAdapter(adapter.key)"
                        >
                          <template #leading>
                            <AdapterIcon
                              :adapter-key="adapter.key"
                              class="size-4"
                            />
                          </template>
                          <p class="truncate font-medium">
                            {{ adapter.displayName }}
                          </p>
                          <p
                            v-if="adapter.version"
                            class="mt-0.5 font-mono text-xs font-normal text-muted-foreground"
                          >
                            v{{ adapter.version }}
                          </p>
                          <template
                            v-if="
                              isExperimentalConnection({
                                adapterKey: adapter.key,
                              })
                            "
                            #trailing
                          >
                            <ExperimentalBadge />
                          </template>
                        </InsetGroupItem>
                      </InsetGroup>
                    </section>

                    <form
                      v-else-if="createDialogStep === 2"
                      id="new-connection-form"
                      @submit.prevent="handleCreateConnection"
                    >
                      <ConnectionCreateForm
                        ref="createForm"
                        :adapters="adapters"
                        :initial-adapter-key="selectedAdapterKey"
                        lock-adapter
                        :show-active-switch="false"
                        :disabled="isCreating"
                        id-prefix="new-conn"
                      />
                    </form>

                    <section v-else class="space-y-4">
                      <p class="px-1 text-sm font-medium">
                        {{ createdConnection?.name }}
                      </p>
                      <AdapterProductDirectionOptions
                        :adapter-name="
                          getAdapterName(createdConnection?.adapterKey)
                        "
                        :can-import="
                          canAdapterImportAll(
                            adapters.find(
                              (item) =>
                                item.key === createdConnection?.adapterKey,
                            ),
                          )
                        "
                        :can-publish="
                          canAdapterPublish(
                            adapters.find(
                              (item) =>
                                item.key === createdConnection?.adapterKey,
                            ),
                          )
                        "
                        :can-sync="
                          canAdapterSync(
                            adapters.find(
                              (item) =>
                                item.key === createdConnection?.adapterKey,
                            ),
                          )
                        "
                        @import="continueWithConnection('import')"
                        @publish="continueWithConnection('publish')"
                        @sync="continueWithConnection('sync')"
                      />
                      <div class="flex justify-end">
                        <Button
                          variant="ghost"
                          @click="continueWithConnection('later')"
                        >
                          {{
                            $t("marketplace.connectionPage.products.setupLater")
                          }}
                        </Button>
                      </div>
                    </section>
                  </div>
                </Transition>
              </template>
            </div>

            <DialogFooter
              v-if="
                adapters.length > 0 &&
                !connectionQuotaReached &&
                createDialogStep !== 3
              "
              variant="bar"
            >
              <DialogClose as-child>
                <Button type="button" variant="outline">{{
                  $t("common.cancel")
                }}</Button>
              </DialogClose>
              <Button
                v-if="createDialogStep === 1"
                type="button"
                :disabled="!selectedAdapterKey"
                @click="advanceCreateStep"
              >
                {{ $t("common.next") }}
              </Button>
              <div v-else class="flex items-center gap-2">
                <Button
                  type="button"
                  variant="outline"
                  :disabled="isCreating"
                  @click="backCreateStep"
                >
                  {{ $t("common.back") }}
                </Button>
                <Button
                  type="submit"
                  form="new-connection-form"
                  :disabled="isCreating"
                >
                  <Spinner v-if="isCreating" class="mr-2" />
                  {{ $t("marketplace.marketplacePage.createConnection") }}
                </Button>
              </div>
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
        <InsetGroup>
          <InsetGroupItem
            v-for="conn in connections"
            :key="conn.id"
            show-chevron
            @click="$router.push(`/adapter/${conn.id}`)"
          >
            <template #leading>
              <AdapterIcon :adapter-key="conn.adapterKey" class="size-4" />
            </template>
            <p class="truncate">
              {{ conn.name }}
              <ExperimentalBadge
                v-if="isExperimentalConnection(conn)"
                class="ml-1.5"
              />
            </p>
            <p
              class="text-muted-foreground mt-0.5 truncate text-xs font-normal"
            >
              {{ connectionRowSubtitle(conn) }}
            </p>
            <template #trailing>
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    reveal-on-row-hover
                    class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                    :disabled="deletingConnectionId === conn.id"
                    :aria-label="$t('common.delete')"
                    @click.stop
                  >
                    <Spinner v-if="deletingConnectionId === conn.id" />
                    <Trash2 v-else class="size-4" />
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>{{
                      $t("marketplace.marketplacePage.deleteConnectionTitle")
                    }}</AlertDialogTitle>
                    <AlertDialogDescription>
                      {{
                        $t(
                          "marketplace.marketplacePage.deleteConnectionDescription",
                          { name: conn.name },
                        )
                      }}
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{{
                      $t("common.cancel")
                    }}</AlertDialogCancel>
                    <AlertDialogAction
                      variant="destructive"
                      @click="handleDeleteConnection(conn.id)"
                      :disabled="deletingConnectionId != null"
                    >
                      <Spinner
                        v-if="deletingConnectionId === conn.id"
                        class="mr-2"
                      />
                      {{ $t("common.delete") }}
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            </template>
          </InsetGroupItem>
        </InsetGroup>
      </PageSection>
    </div>
  </AppPageChrome>
</template>
