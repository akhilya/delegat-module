<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import { Wand2 } from "lucide-vue-next";
import { mapActions } from "vuex";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import { PAGE_TOOLS } from "@/router";
import { toolDescription, toolTitle } from "@/lib/toolsCatalog";

export default {
  components: {
    AppPageChrome,
    Button,
    Spinner,
    EmptyState,
    Input,
    PasswordInput,
    SettingsFormField,
    FieldGroup,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    AdapterIcon,
    Wand2,
  },
  data() {
    return {
      loading: true,
      running: false,
      entry: null,
      connections: [],
      connectionId: null,
      paramValues: {},
      table: null,
    };
  },
  computed: {
    breadcrumbs() {
      return [
        { label: this.$t("pages.toolsPage.breadcrumb"), to: PAGE_TOOLS },
        { label: this.entry ? toolTitle(this.entry, this.$t) : this.$t("pages.toolJob.breadcrumb") },
      ];
    },
    matchingConnections() {
      const key = this.entry?.adapterKey;
      if (!key) return this.connections;
      return this.connections.filter((c) => c.adapterKey === key);
    },
    paramFields() {
      return this.entry?.paramsSchema || [];
    },
    canRun() {
      if (!this.entry || this.entry.mode !== "job" || this.entry.available === false) return false;
      if (this.entry.requiresConnection && !this.connectionId) return false;
      for (const field of this.paramFields) {
        if (!field.isRequired) continue;
        const value = this.paramValues[field.key];
        if (value == null || String(value).trim() === "") return false;
      }
      return true;
    },
  },
  watch: {
    "$route.params.toolId"() {
      this.load();
    },
  },
  async mounted() {
    await this.load();
  },
  methods: {
    ...mapActions(["getConnections"]),
    toolTitle,
    toolDescription,
    goTools() {
      this.$router.push(PAGE_TOOLS);
    },
    unwrapList(response) {
      const data = extractResponseData(response, []);
      if (Array.isArray(data)) return data;
      if (Array.isArray(data?.items)) return data.items;
      return [];
    },
    async load() {
      this.loading = true;
      this.entry = null;
      this.table = null;
      const id = this.$route.params.toolId;
      try {
        const response = await http.get(`/tools/${encodeURIComponent(id)}`, { cache: false });
        const entry = extractResponseData({ data: response.data }, null);
        this.entry = entry?.value && typeof entry.value === "object" ? entry.value : entry;
        if (this.entry?.mode === "page" && this.entry.path) {
          await this.$router.replace(this.entry.path);
          return;
        }
        await this.loadConnections();
        this.seedParams();
      } catch (error) {
        displaySonnerError(error);
        this.entry = null;
      } finally {
        this.loading = false;
      }
    },
    seedParams() {
      const values = {};
      for (const field of this.paramFields) {
        values[field.key] = field.defaultValue || "";
      }
      this.paramValues = values;
      if (this.matchingConnections.length === 1) {
        this.connectionId = this.matchingConnections[0].id;
      }
    },
    loadConnections() {
      return new Promise((resolve) => {
        this.getConnections({
          onSuccess: (response) => {
            this.connections = this.unwrapList(response);
            resolve();
          },
          onError: (error) => {
            displaySonnerError(error);
            this.connections = [];
            resolve();
          },
        });
      });
    },
    async run() {
      if (!this.canRun || this.running || !this.entry) return;
      this.running = true;
      this.table = null;
      try {
        const response = await http.post(
          `/tools/${encodeURIComponent(this.entry.id)}/run`,
          {
            connectionId: this.connectionId,
            parameters: this.paramValues,
          },
          { cache: false, timeout: 5 * 60 * 1000 },
        );
        const result = extractResponseData({ data: response.data }, {});
        const body = result?.value && typeof result.value === "object" ? result.value : result;
        this.$store.dispatch("operations/openPanel");
        if (body.table?.rows) this.table = body.table;
        displaySonnerSuccess(body.message || this.$t("pages.toolJob.runStarted"));
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.running = false;
      }
    },
  },
};
</script>

<template>
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <EmptyState
      v-if="loading"
      loading
      class="min-h-0 flex-1"
    />
    <EmptyState
      v-else-if="!entry"
      :title="$t('pages.toolJob.notFoundTitle')"
      :description="$t('pages.toolJob.notFoundDescription')"
    >
      <template #icon>
        <Wand2 />
      </template>
      <Button class="mt-4" variant="outline" @click="goTools">
        {{ $t("pages.toolsPage.breadcrumb") }}
      </Button>
    </EmptyState>
    <div v-else class="flex w-full max-w-xl flex-col">
      <div class="mb-6 flex items-start gap-3">
        <AdapterIcon
          v-if="entry.adapterKey"
          :adapter-key="entry.adapterKey"
          class="mt-0.5 size-5"
        />
        <Wand2 v-else class="text-muted-foreground mt-0.5 size-5" />
        <p class="text-sm text-muted-foreground">
          {{ toolDescription(entry, $t) }}
        </p>
      </div>

      <FieldGroup class="gap-4">
        <SettingsFormField
          v-if="entry.requiresConnection"
          :label="$t('pages.toolJob.connectionLabel')"
          label-for="tool-job-connection"
          :description="$t('pages.toolJob.connectionHint')"
        >
          <Select
            :model-value="connectionId != null ? String(connectionId) : undefined"
            @update:model-value="connectionId = $event ? Number($event) : null"
          >
            <SelectTrigger id="tool-job-connection" class="w-full">
              <SelectValue :placeholder="$t('pages.toolJob.connectionPlaceholder')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="conn in matchingConnections"
                :key="conn.id"
                :value="String(conn.id)"
              >
                {{ conn.name }}
              </SelectItem>
            </SelectContent>
          </Select>
        </SettingsFormField>
        <p
          v-if="entry.requiresConnection && matchingConnections.length === 0"
          class="text-sm text-muted-foreground"
        >
          {{ $t("pages.toolJob.noConnections") }}
        </p>

        <SettingsFormField
          v-for="field in paramFields"
          :key="field.key"
          :label="field.displayName || field.key"
          :label-for="`tool-param-${field.key}`"
          :required="field.isRequired"
          :description="field.description || ''"
        >
          <PasswordInput
            v-if="field.isSecret"
            :id="`tool-param-${field.key}`"
            v-model="paramValues[field.key]"
            :placeholder="field.placeholder || ''"
            autocomplete="off"
          />
          <Input
            v-else
            :id="`tool-param-${field.key}`"
            v-model="paramValues[field.key]"
            type="text"
            :placeholder="field.placeholder || ''"
            autocomplete="off"
          />
        </SettingsFormField>
      </FieldGroup>

      <Button class="mt-6 self-start" :disabled="!canRun || running" @click="run">
        <Spinner v-if="running" />
        {{ $t("pages.toolJob.run") }}
      </Button>

      <div
        v-if="table"
        class="mt-8 overflow-hidden rounded-xl border border-border/70 bg-card"
      >
        <Table>
          <TableHeader>
            <TableRow class="hover:bg-transparent">
              <TableHead
                v-for="col in table.columns"
                :key="col"
                class="truncate px-3 py-2"
              >
                {{ col }}
              </TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="(row, index) in table.rows" :key="index">
              <TableCell
                v-for="(cell, cellIndex) in row"
                :key="cellIndex"
                class="max-w-0 truncate px-3 py-2"
              >
                {{ cell }}
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </div>
  </AppPageChrome>
</template>
