<script>
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import { PageHeader } from "@/components/ui/page-header";
import Input from "@/components/ui/input/Input.vue";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { LogTableFrame, LogDateCell, LogRefreshButton } from "@/components/logs";
import ApiRequestLogsPanel from "@/components/logs/ApiRequestLogsPanel.vue";
import { http, loadMe } from "@/main";
import { ME_UPDATED_EVENT } from "@/lib/meCache";
import {
  displaySonnerError,
  displaySonnerSuccess,
} from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import {
  Copy,
  ExternalLink,
  KeyRound,
  Loader2,
  ScrollText,
  Trash2,
} from "lucide-vue-next";

export default {
  components: {
    PageSection,
    Button,
    Badge,
    PageHeader,
    Input,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Tabs,
    TabsContent,
    TabsList,
    TabsTrigger,
    LogTableFrame,
    LogDateCell,
    LogRefreshButton,
    ApiRequestLogsPanel,
    Copy,
    ExternalLink,
    KeyRound,
    Loader2,
    ScrollText,
    Trash2,
  },
  setup() {
    const { timezone, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { storeTimezone: timezone };
  },
  data() {
    return {
      tab: "keys",
      apiKeys: [],
      apiKeysLoading: false,
      newKeyName: "",
      isCreatingKey: false,
      createdPlaintext: null,
      revokingId: null,
      logsLoading: false,
      logApiRequestsEnabled: false,
      publicApiEnabled: true,
      _onMeUpdated: null,
    };
  },
  computed: {
    showLogsTab() {
      return this.publicApiEnabled === true && this.logApiRequestsEnabled === true;
    },
  },
  methods: {
    applyLoggingFlag(me) {
      this.logApiRequestsEnabled = me?.logApiRequestsEnabled === true;
      this.publicApiEnabled = me?.publicApiEnabled !== false;
      if (!this.showLogsTab && this.tab === "logs") this.tab = "keys";
    },

    refreshLogs() {
      this.$refs.logsPanel?.refresh?.();
    },

    async fetchApiKeys() {
      this.apiKeysLoading = true;
      try {
        const response = await http.get("/api-keys", { cache: false });
        const data = response.data?.value ?? response.data;
        this.apiKeys = data?.items || [];
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.apiKeysLoading = false;
      }
    },

    async createApiKey() {
      const name = this.newKeyName.trim();
      if (!name) return;
      this.isCreatingKey = true;
      try {
        const response = await http.post("/api-keys", { name }, { cache: false });
        const data = response.data?.value ?? response.data;
        this.createdPlaintext = data?.plaintext || null;
        this.newKeyName = "";
        displaySonnerSuccess(this.$t("pages.apiPage.keys.created"));
        await this.fetchApiKeys();
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.isCreatingKey = false;
      }
    },

    async copyPlaintext() {
      if (!this.createdPlaintext) return;
      try {
        await navigator.clipboard.writeText(this.createdPlaintext);
        displaySonnerSuccess(this.$t("pages.apiPage.keys.copied"));
      } catch {
        displaySonnerError(this.$t("pages.apiPage.keys.copyError"));
      }
    },

    async revokeApiKey(id) {
      const ok = await confirm({
        title: this.$t("pages.apiPage.keys.revokeTitle"),
        description: this.$t("pages.apiPage.keys.revokeDescription"),
      });
      if (!ok) return;
      this.revokingId = id;
      try {
        await http.delete(`/api-keys/${id}`);
        displaySonnerSuccess(this.$t("pages.apiPage.keys.revoked"));
        if (this.createdPlaintext) this.createdPlaintext = null;
        await this.fetchApiKeys();
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.revokingId = null;
      }
    },

    async onTabChange(value) {
      if (value === "logs" && !this.showLogsTab) {
        this.tab = "keys";
        return;
      }
      this.tab = value;
      if (value === "logs") {
        await this.$nextTick();
        this.$refs.logsPanel?.ensureLoaded?.();
      }
    },
  },
  async mounted() {
    this._onMeUpdated = (event) => {
      if (event?.detail && typeof event.detail === "object") {
        this.applyLoggingFlag(event.detail);
      }
    };
    window.addEventListener(ME_UPDATED_EVENT, this._onMeUpdated);

    try {
      this.applyLoggingFlag(await loadMe());
    } catch {
      this.applyLoggingFlag(null);
    }
    await this.fetchApiKeys();
  },
  beforeUnmount() {
    if (this._onMeUpdated) {
      window.removeEventListener(ME_UPDATED_EVENT, this._onMeUpdated);
    }
  },
};
</script>

<template>
  <div
    class="flex min-h-0 flex-col pb-4"
    :class="tab === 'logs' && showLogsTab ? 'flex-1 overflow-hidden' : ''"
  >
    <PageHeader
      class="shrink-0"
      :title="$t('routes.api')"
      :description="$t('pages.apiPage.description')"
    >
      <a
        href="/swagger"
        target="_blank"
        rel="noopener noreferrer"
        class="inline-flex h-9 items-center gap-1.5 rounded-md border bg-background px-3 text-sm font-medium hover:bg-muted"
      >
        {{ $t("pages.apiPage.openSwagger") }}
        <ExternalLink class="size-3.5" />
      </a>
    </PageHeader>

    <div
      v-if="!publicApiEnabled"
      class="mt-3 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm text-foreground"
    >
      {{ $t("pages.apiPage.disabledBanner") }}
    </div>

    <Tabs
      v-if="showLogsTab"
      :model-value="tab"
      class="mt-2 flex min-h-0 flex-1 flex-col overflow-hidden"
      :class="tab === 'logs' ? 'min-h-0' : ''"
      @update:model-value="onTabChange"
    >
      <div class="mb-3 flex shrink-0 flex-wrap items-center justify-between gap-3">
        <TabsList>
          <TabsTrigger value="keys" class="gap-1.5">
            <KeyRound class="size-3.5" />
            {{ $t("pages.apiPage.tabKeys") }}
          </TabsTrigger>
          <TabsTrigger value="logs" class="gap-1.5">
            <ScrollText class="size-3.5" />
            {{ $t("pages.apiPage.tabLogs") }}
          </TabsTrigger>
        </TabsList>
        <LogRefreshButton
          v-if="tab === 'logs'"
          :loading="logsLoading"
          @click="refreshLogs"
        />
      </div>

      <TabsContent value="keys" class="mt-0 outline-none">
        <div class="keys-panel">
          <PageSection>
            <p class="mb-5 max-w-2xl text-sm text-muted-foreground">
              {{ $t("pages.apiPage.keys.introPrefix") }}
              <code class="text-xs">Authorization: Bearer dk_…</code>.
              {{ $t("pages.apiPage.keys.introSuffix") }}
            </p>

            <div
              v-if="createdPlaintext"
              class="mb-5 flex flex-col gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 p-3 sm:max-w-2xl"
            >
              <div class="text-sm font-medium">{{ $t("pages.apiPage.keys.copyWarning") }}</div>
              <div class="flex flex-wrap items-center gap-2">
                <code class="break-all text-xs">{{ createdPlaintext }}</code>
                <Button variant="outline" size="sm" @click="copyPlaintext">
                  <Copy class="mr-1 size-3.5" />
                  {{ $t("common.copy") }}
                </Button>
              </div>
            </div>

            <div class="mb-5 flex flex-wrap items-end gap-3 sm:max-w-2xl">
              <div class="grid min-w-[220px] flex-1 gap-1.5">
                <label class="text-sm font-medium" for="api-key-name">{{ $t("pages.apiPage.keys.nameLabel") }}</label>
                <Input
                  id="api-key-name"
                  v-model="newKeyName"
                  :placeholder="$t('pages.apiPage.keys.namePlaceholder')"
                  :disabled="isCreatingKey"
                  @keydown.enter="createApiKey"
                />
              </div>
              <Button :disabled="isCreatingKey || !newKeyName.trim()" @click="createApiKey">
                <Loader2 v-if="isCreatingKey" class="mr-2 size-4 animate-spin" />
                <KeyRound v-else class="mr-2 size-4" />
                {{ $t("pages.apiPage.keys.create") }}
              </Button>
            </div>

            <div v-if="apiKeysLoading" class="flex items-center gap-2 text-sm text-muted-foreground">
              <Loader2 class="size-4 animate-spin" />
              {{ $t("pages.apiPage.keys.loading") }}
            </div>

            <PageSection
              v-else
              class="!p-0"
              :empty="apiKeys.length === 0"
              :empty-icon="KeyRound"
              :empty-title="$t('pages.apiPage.keys.emptyTitle')"
              :empty-description="$t('pages.apiPage.keys.emptyDescription')"
            >
              <LogTableFrame v-if="apiKeys.length">
                <Table class="table-fixed">
                  <TableHeader>
                    <TableRow>
                      <TableHead class="min-w-0">{{ $t("pages.apiPage.keys.columnName") }}</TableHead>
                      <TableHead class="w-40">{{ $t("pages.apiPage.keys.columnHint") }}</TableHead>
                      <TableHead class="w-40">{{ $t("pages.apiPage.keys.columnCreated") }}</TableHead>
                      <TableHead class="w-40">{{ $t("pages.apiPage.keys.columnUsed") }}</TableHead>
                      <TableHead class="w-28">{{ $t("pages.apiPage.keys.columnStatus") }}</TableHead>
                      <TableHead class="w-16 text-right" />
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    <TableRow v-for="key in apiKeys" :key="key.id">
                      <TableCell class="font-medium">{{ key.name }}</TableCell>
                      <TableCell>
                        <code class="text-xs text-muted-foreground">{{ key.displayHint }}</code>
                      </TableCell>
                      <TableCell>
                        <LogDateCell :value="key.createdAt" :timezone="storeTimezone" />
                      </TableCell>
                      <TableCell>
                        <LogDateCell
                          v-if="key.lastUsedAt"
                          :value="key.lastUsedAt"
                          :timezone="storeTimezone"
                        />
                        <span v-else class="text-sm text-muted-foreground">-</span>
                      </TableCell>
                      <TableCell>
                        <Badge :variant="key.isRevoked ? 'secondary' : 'default'">
                          {{
                            key.isRevoked
                              ? $t("pages.apiPage.keys.statusRevoked")
                              : $t("pages.apiPage.keys.statusActive")
                          }}
                        </Badge>
                      </TableCell>
                      <TableCell class="text-right">
                        <Button
                          v-if="!key.isRevoked"
                          variant="ghost"
                          size="icon"
                          :disabled="revokingId === key.id"
                          @click="revokeApiKey(key.id)"
                        >
                          <Loader2 v-if="revokingId === key.id" class="size-4 animate-spin" />
                          <Trash2 v-else class="size-4" />
                        </Button>
                      </TableCell>
                    </TableRow>
                  </TableBody>
                </Table>
              </LogTableFrame>
            </PageSection>
          </PageSection>
        </div>
      </TabsContent>

      <TabsContent
        value="logs"
        class="mt-0 flex min-h-0 flex-1 flex-col overflow-hidden outline-none data-[state=inactive]:hidden"
      >
        <ApiRequestLogsPanel
          ref="logsPanel"
          class="mb-4 min-h-0 flex-1"
          :api-keys="apiKeys"
          @loading="(v) => (logsLoading = v)"
        />
      </TabsContent>
    </Tabs>

    <!-- Logging off: same keys UI, no tab bar -->
    <div v-else class="keys-panel">
      <PageSection>
        <p class="mb-5 max-w-2xl text-sm text-muted-foreground">
          {{ $t("pages.apiPage.keys.introPrefix") }}
          <code class="text-xs">Authorization: Bearer dk_…</code>.
          {{ $t("pages.apiPage.keys.introSuffix") }}
        </p>

        <div
          v-if="createdPlaintext"
          class="mb-5 flex flex-col gap-2 rounded-md border border-amber-500/40 bg-amber-500/10 p-3 sm:max-w-2xl"
        >
          <div class="text-sm font-medium">{{ $t("pages.apiPage.keys.copyWarning") }}</div>
          <div class="flex flex-wrap items-center gap-2">
            <code class="break-all text-xs">{{ createdPlaintext }}</code>
            <Button variant="outline" size="sm" @click="copyPlaintext">
              <Copy class="mr-1 size-3.5" />
              {{ $t("common.copy") }}
            </Button>
          </div>
        </div>

        <div class="mb-5 flex flex-wrap items-end gap-3 sm:max-w-2xl">
          <div class="grid min-w-[220px] flex-1 gap-1.5">
            <label class="text-sm font-medium" for="api-key-name-solo">{{ $t("pages.apiPage.keys.nameLabel") }}</label>
            <Input
              id="api-key-name-solo"
              v-model="newKeyName"
              :placeholder="$t('pages.apiPage.keys.namePlaceholder')"
              :disabled="isCreatingKey"
              @keydown.enter="createApiKey"
            />
          </div>
          <Button :disabled="isCreatingKey || !newKeyName.trim()" @click="createApiKey">
            <Loader2 v-if="isCreatingKey" class="mr-2 size-4 animate-spin" />
            <KeyRound v-else class="mr-2 size-4" />
            {{ $t("pages.apiPage.keys.create") }}
          </Button>
        </div>

        <div v-if="apiKeysLoading" class="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 class="size-4 animate-spin" />
          {{ $t("pages.apiPage.keys.loading") }}
        </div>

        <PageSection
          v-else
          class="!p-0"
          :empty="apiKeys.length === 0"
          :empty-icon="KeyRound"
          :empty-title="$t('pages.apiPage.keys.emptyTitle')"
          :empty-description="$t('pages.apiPage.keys.emptyDescription')"
        >
          <LogTableFrame v-if="apiKeys.length">
            <Table class="table-fixed">
              <TableHeader>
                <TableRow>
                  <TableHead class="min-w-0">{{ $t("pages.apiPage.keys.columnName") }}</TableHead>
                  <TableHead class="w-40">{{ $t("pages.apiPage.keys.columnHint") }}</TableHead>
                  <TableHead class="w-40">{{ $t("pages.apiPage.keys.columnCreated") }}</TableHead>
                  <TableHead class="w-40">{{ $t("pages.apiPage.keys.columnUsed") }}</TableHead>
                  <TableHead class="w-28">{{ $t("pages.apiPage.keys.columnStatus") }}</TableHead>
                  <TableHead class="w-16 text-right" />
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="key in apiKeys" :key="key.id">
                  <TableCell class="font-medium">{{ key.name }}</TableCell>
                  <TableCell>
                    <code class="text-xs text-muted-foreground">{{ key.displayHint }}</code>
                  </TableCell>
                  <TableCell>
                    <LogDateCell :value="key.createdAt" :timezone="storeTimezone" />
                  </TableCell>
                  <TableCell>
                    <LogDateCell
                      v-if="key.lastUsedAt"
                      :value="key.lastUsedAt"
                      :timezone="storeTimezone"
                    />
                    <span v-else class="text-sm text-muted-foreground">-</span>
                  </TableCell>
                  <TableCell>
                    <Badge :variant="key.isRevoked ? 'secondary' : 'default'">
                      {{
                        key.isRevoked
                          ? $t("pages.apiPage.keys.statusRevoked")
                          : $t("pages.apiPage.keys.statusActive")
                      }}
                    </Badge>
                  </TableCell>
                  <TableCell class="text-right">
                    <Button
                      v-if="!key.isRevoked"
                      variant="ghost"
                      size="icon"
                      :disabled="revokingId === key.id"
                      @click="revokeApiKey(key.id)"
                    >
                      <Loader2 v-if="revokingId === key.id" class="size-4 animate-spin" />
                      <Trash2 v-else class="size-4" />
                    </Button>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </LogTableFrame>
        </PageSection>
      </PageSection>
    </div>
  </div>
</template>
