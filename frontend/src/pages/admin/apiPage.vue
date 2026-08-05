<script>
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import Input from "@/components/ui/input/Input.vue";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { LogTableFrame, LogDateCell } from "@/components/logs";
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
  Trash2,
} from "lucide-vue-next";

export default {
  components: {
    PageSection,
    Button,
    Badge,
    Input,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    LogTableFrame,
    LogDateCell,
    ApiRequestLogsPanel,
    Copy,
    ExternalLink,
    KeyRound,
    Loader2,
    Trash2,
  },
  setup() {
    const { timezone, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { storeTimezone: timezone };
  },
  data() {
    return {
      apiKeys: [],
      apiKeysLoading: false,
      newKeyName: "",
      isCreatingKey: false,
      createdPlaintext: null,
      revokingId: null,
      logApiRequestsEnabled: false,
      publicApiEnabled: true,
      _onMeUpdated: null,
    };
  },
  computed: {
    /** Driven by SettingsShell navbar tabs via ?tab=logs */
    tab() {
      return this.$route.query.tab === "logs" ? "logs" : "keys";
    },
    logsEnabled() {
      return this.publicApiEnabled === true && this.logApiRequestsEnabled === true;
    },
    showKeys() {
      return this.tab === "keys";
    },
    showLogs() {
      return this.tab === "logs";
    },
  },
  watch: {
    showLogs(isLogs) {
      if (isLogs && this.logsEnabled) {
        this.$nextTick(() => this.$refs.logsPanel?.ensureLoaded?.());
      }
    },
  },
  methods: {
    applyLoggingFlag(me) {
      this.logApiRequestsEnabled = me?.logApiRequestsEnabled === true;
      this.publicApiEnabled = me?.publicApiEnabled !== false;
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
    if (this.showLogs && this.logsEnabled) {
      await this.$nextTick();
      this.$refs.logsPanel?.ensureLoaded?.();
    }
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
    :class="showLogs ? 'flex-1 overflow-hidden' : ''"
  >
    <div
      v-if="!publicApiEnabled"
      class="mb-4 rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm text-foreground"
    >
      {{ $t("pages.apiPage.disabledBanner") }}
    </div>

    <div v-show="showKeys" class="keys-panel">
      <PageSection>
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

        <div class="mb-5 flex flex-wrap items-end gap-3">
          <div class="grid min-w-[220px] max-w-md flex-1 gap-1.5">
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
          <Button
            as="a"
            href="/swagger"
            target="_blank"
            rel="noopener noreferrer"
            variant="link"
            class="h-9 px-0"
          >
            {{ $t("pages.apiPage.openSwagger") }}
            <ExternalLink class="size-3.5" />
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
                  <TableHead class="w-36">{{ $t("pages.apiPage.keys.columnHint") }}</TableHead>
                  <TableHead class="w-36">{{ $t("pages.apiPage.keys.columnCreated") }}</TableHead>
                  <TableHead class="w-48 whitespace-normal">{{ $t("pages.apiPage.keys.columnUsed") }}</TableHead>
                  <TableHead class="w-28">{{ $t("pages.apiPage.keys.columnStatus") }}</TableHead>
                  <TableHead class="w-14 text-right" />
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

    <div
      v-if="showLogs && !logsEnabled"
      class="rounded-md border border-dashed px-4 py-8 text-center text-sm text-muted-foreground"
    >
      {{ $t("pages.apiPage.logs.disabledHint") }}
    </div>

    <ApiRequestLogsPanel
      v-else-if="logsEnabled"
      v-show="showLogs"
      ref="logsPanel"
      class="mb-4 min-h-0 flex-1"
      :class="showLogs ? '' : '!hidden'"
      :api-keys="apiKeys"
    />
  </div>
</template>
