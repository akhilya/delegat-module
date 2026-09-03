<script>
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import Badge from "@/components/ui/badge/Badge.vue";
import Input from "@/components/ui/input/Input.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
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
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { confirm } from "@/composables/useConfirm";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { PAGE_PUBLIC_API_TRY } from "@/router";
import { RouterLink } from "vue-router";
import {
  Copy,
  ExternalLink,
  KeyRound,
  ScrollText,
  Trash2,
} from "lucide-vue-next";

export default {
  components: {
    Spinner,
    PageSection,
    Button,
    Badge,
    Input,
    SettingsFormField,
    FieldGroup,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
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
    ScrollText,
    Trash2,
    RouterLink,
  },
  setup() {
    const { timezone, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { storeTimezone: timezone, PAGE_PUBLIC_API_TRY };
  },
  data() {
    return {
      apiKeys: [],
      apiKeysLoading: false,
      newKeyName: "",
      FIELD_LIMITS,
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
      return (
        this.publicApiEnabled === true && this.logApiRequestsEnabled === true
      );
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
      const nameIssue = fieldLimitIssue(
        this.newKeyName,
        FIELD_LIMITS.apiKeyName,
      );
      if (nameIssue === "empty") return;
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.apiKeyName }),
        );
        return;
      }
      const name = this.newKeyName.trim();
      this.isCreatingKey = true;
      try {
        const response = await http.post(
          "/api-keys",
          { name },
          { cache: false },
        );
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
  <div class="flex flex-col pb-4">
    <div v-if="!publicApiEnabled" class="mb-6 max-w-xl">
      <InsetGroup>
        <InsetGroupItem as="div">
          <template #leading>
            <KeyRound class="size-4" />
          </template>
          <p>{{ $t("pages.apiPage.disabledBanner") }}</p>
        </InsetGroupItem>
      </InsetGroup>
    </div>

    <div v-show="showKeys" class="keys-panel">
      <PageSection>
        <section v-if="createdPlaintext" class="mb-6 max-w-xl">
          <InsetGroupLabel>{{
            $t("pages.apiPage.keys.copyWarning")
          }}</InsetGroupLabel>
          <InsetGroup>
            <InsetGroupItem as="div">
              <code class="block break-all text-xs">{{
                createdPlaintext
              }}</code>
              <template #trailing>
                <Button variant="outline" size="sm" @click="copyPlaintext">
                  <Copy class="mr-1 size-3.5" />
                  {{ $t("common.copy") }}
                </Button>
              </template>
            </InsetGroupItem>
          </InsetGroup>
        </section>

        <form class="mb-6 max-w-xl" @submit.prevent="createApiKey">
          <FieldGroup class="gap-3 sm:flex-row sm:items-end">
            <SettingsFormField
              class="min-w-0 flex-1"
              :label="$t('pages.apiPage.keys.nameLabel')"
              label-for="api-key-name"
            >
              <Input
                id="api-key-name"
                v-model="newKeyName"
                :placeholder="$t('pages.apiPage.keys.namePlaceholder')"
                :disabled="isCreatingKey"
                :maxlength="FIELD_LIMITS.apiKeyName"
              />
            </SettingsFormField>
            <Button
              type="submit"
              class="shrink-0"
              :disabled="isCreatingKey || !newKeyName.trim()"
            >
              <Spinner v-if="isCreatingKey" class="mr-2" />
              <KeyRound v-else class="mr-2 size-4" />
              {{ $t("pages.apiPage.keys.create") }}
            </Button>
          </FieldGroup>
          <div class="mt-2 flex flex-wrap items-center gap-x-4 gap-y-1 px-1">
            <Button
              as="a"
              href="/swagger"
              target="_blank"
              rel="noopener noreferrer"
              variant="link"
              class="h-auto px-0 text-xs"
            >
              {{ $t("pages.apiPage.openSwagger") }}
              <ExternalLink class="size-3.5" />
            </Button>
            <Button variant="link" class="h-auto px-0 text-xs" as-child>
              <RouterLink :to="PAGE_PUBLIC_API_TRY">
                {{ $t("pages.apiPage.openTryTool") }}
              </RouterLink>
            </Button>
          </div>
        </form>

        <div
          v-if="apiKeysLoading"
          class="flex min-h-[12rem] flex-1 items-center justify-center"
        >
          <Spinner size="2xl" class="text-muted-foreground" />
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
                  <TableHead class="min-w-0">{{
                    $t("pages.apiPage.keys.columnName")
                  }}</TableHead>
                  <TableHead class="w-36">{{
                    $t("pages.apiPage.keys.columnHint")
                  }}</TableHead>
                  <TableHead class="w-36">{{
                    $t("pages.apiPage.keys.columnCreated")
                  }}</TableHead>
                  <TableHead class="w-48 whitespace-normal">{{
                    $t("pages.apiPage.keys.columnUsed")
                  }}</TableHead>
                  <TableHead class="w-28">{{
                    $t("pages.apiPage.keys.columnStatus")
                  }}</TableHead>
                  <TableHead class="w-14 text-right" />
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="key in apiKeys" :key="key.id">
                  <TableCell class="max-w-0 font-medium">
                    <span class="block truncate" :title="key.name">{{
                      key.name
                    }}</span>
                  </TableCell>
                  <TableCell>
                    <code class="text-xs text-muted-foreground">{{
                      key.displayHint
                    }}</code>
                  </TableCell>
                  <TableCell>
                    <LogDateCell
                      :value="key.createdAt"
                      :timezone="storeTimezone"
                    />
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
                      <Spinner v-if="revokingId === key.id" />
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

    <div v-if="showLogs && !logsEnabled" class="max-w-xl">
      <InsetGroup>
        <InsetGroupItem as="div">
          <template #leading>
            <ScrollText class="size-4" />
          </template>
          <p>{{ $t("pages.apiPage.logs.disabledHint") }}</p>
        </InsetGroupItem>
      </InsetGroup>
    </div>

    <ApiRequestLogsPanel
      v-else-if="logsEnabled"
      v-show="showLogs"
      ref="logsPanel"
      :api-keys="apiKeys"
    />
  </div>
</template>
