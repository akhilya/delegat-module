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
import { Wand2 } from "lucide-vue-next";
import { mapActions } from "vuex";
import { http } from "@/main";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { PAGE_TOOLS, setPageTitle } from "@/router";
import {
  isQuickRunTool,
  toolConfirmTitleKey,
  toolJobConfirmDescriptionKey,
  toolDescription,
  isValidImportProductLimit,
  toolStageClass,
  toolStageLabelKey,
  toolTitle,
} from "@/lib/toolsCatalog";
import { adapterHasCapability } from "@/lib/adapterCapabilities";
import { getCachedMe } from "@/lib/meCache";
import { hasPermission, PERMS } from "@/lib/permissions";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";

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
    Wand2,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
  },
  data() {
    return {
      loading: true,
      running: false,
      entry: null,
      connections: [],
      adapters: [],
      connectionId: null,
      paramValues: {},
      table: null,
      confirmOpen: false,
    };
  },
  computed: {
    matchingConnections() {
      const key = this.entry?.adapterKey;
      const cap = this.entry?.requiredCapability;
      let rows = this.connections.filter((c) => c.isActive !== false);
      if (key) rows = rows.filter((c) => c.adapterKey === key);
      if (cap) {
        rows = rows.filter((c) => {
          const adapter = this.adapters.find((a) => a.key === c.adapterKey);
          return adapterHasCapability(adapter, cap);
        });
      }
      return rows;
    },
    paramFields() {
      return this.entry?.paramsSchema || [];
    },
    canRun() {
      if (
        !this.entry ||
        this.entry.mode !== "job" ||
        this.entry.available === false
      )
        return false;
      if (!hasPermission(getCachedMe(), PERMS.MarketplaceManage)) return false;
      if (this.entry.requiresConnection && !this.connectionId) return false;
      for (const field of this.paramFields) {
        if (!field.isRequired) continue;
        const value = this.paramValues[field.key];
        if (value == null || String(value).trim() === "") return false;
      }
      if (
        this.entry.id === "import-products" &&
        !isValidImportProductLimit(this.paramValues.maxProducts)
      ) {
        return false;
      }
      return true;
    },
    confirmTitle() {
      const key = toolConfirmTitleKey(this.entry);
      return key ? this.$t(key) : this.$t("pages.toolJob.run");
    },
    confirmDescription() {
      const key = toolJobConfirmDescriptionKey(this.entry);
      return key ? this.$t(key) : "";
    },
    stageLabelKey() {
      return toolStageLabelKey(this.entry);
    },
    stageClass() {
      return toolStageClass(this.entry);
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
    ...mapActions(["getConnections", "getAvailableAdapters"]),
    toolTitle,
    toolDescription,
    paramLabel(field) {
      if (this.entry?.id === "import-products") {
        const key =
          field.key === "maxProducts"
            ? "pages.toolJob.maxProducts"
            : field.key === "targetCategoryId"
              ? "pages.toolJob.targetCategoryId"
              : null;
        if (key) return this.$t(key);
      }
      return field.displayName || field.key;
    },
    paramDescription(field) {
      if (this.entry?.id === "import-products") {
        const key =
          field.key === "maxProducts"
            ? "pages.toolJob.maxProductsHint"
            : field.key === "targetCategoryId"
              ? "pages.toolJob.targetCategoryIdHint"
              : null;
        if (key) return this.$t(key);
      }
      return field.description || "";
    },
    paramError(field) {
      if (
        this.entry?.id === "import-products" &&
        field.key === "maxProducts" &&
        !isValidImportProductLimit(this.paramValues[field.key])
      ) {
        return this.$t("pages.toolJob.maxProductsInvalid");
      }
      return "";
    },
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
        const response = await http.get(`/tools/${encodeURIComponent(id)}`, {
          cache: false,
        });
        const entry = extractResponseData({ data: response.data }, null);
        this.entry =
          entry?.value && typeof entry.value === "object" ? entry.value : entry;
        if (this.entry) {
          setPageTitle(toolTitle(this.entry, this.$t));
        }
        if (this.entry?.mode === "page" && this.entry.path) {
          await this.$router.replace(this.entry.path);
          return;
        }
        await this.loadConnections();
        await this.loadAdapters();
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
    loadAdapters() {
      return new Promise((resolve) => {
        this.getAvailableAdapters({
          onSuccess: (response) => {
            this.adapters = this.unwrapList(response);
            resolve();
          },
          onError: (error) => {
            displaySonnerError(error);
            this.adapters = [];
            resolve();
          },
        });
      });
    },
    requestRun() {
      if (!this.canRun || this.running || !this.entry) return;
      if (isQuickRunTool(this.entry)) {
        this.confirmOpen = true;
        return;
      }
      this.run();
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
        const body =
          result?.value && typeof result.value === "object"
            ? result.value
            : result;
        this.$store.dispatch("operations/openPanel");
        if (body.table?.rows) this.table = body.table;
        displaySonnerSuccess(
          body.message || this.$t("pages.toolJob.runStarted"),
        );
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
  <AppPageChrome>
    <EmptyState v-if="loading" loading class="min-h-0 flex-1" />
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
    <div v-else class="page-region-in flex w-full max-w-xl flex-col">
      <p
        v-if="stageLabelKey"
        class="mb-2 text-xs font-medium"
        :class="stageClass"
      >
        {{ $t(stageLabelKey) }}
      </p>
      <p class="mb-6 text-sm text-muted-foreground">
        {{ toolDescription(entry, $t) }}
      </p>

      <FieldGroup class="gap-4">
        <SettingsFormField
          v-if="entry.requiresConnection"
          :label="$t('pages.toolJob.connectionLabel')"
          label-for="tool-job-connection"
          :description="$t('pages.toolJob.connectionHint')"
        >
          <Select
            :model-value="
              connectionId != null ? String(connectionId) : undefined
            "
            @update:model-value="connectionId = $event ? Number($event) : null"
          >
            <SelectTrigger id="tool-job-connection" class="w-full">
              <SelectValue
                :placeholder="$t('pages.toolJob.connectionPlaceholder')"
              />
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
          :label="paramLabel(field)"
          :label-for="`tool-param-${field.key}`"
          :required="field.isRequired"
          :description="paramDescription(field)"
          :error="paramError(field)"
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
            :type="field.type === 'number' ? 'number' : 'text'"
            :min="field.key === 'maxProducts' ? 1 : undefined"
            :max="field.key === 'maxProducts' ? 200 : undefined"
            :step="field.key === 'maxProducts' ? 1 : undefined"
            :required="field.isRequired"
            :placeholder="field.placeholder || ''"
            autocomplete="off"
          />
        </SettingsFormField>
      </FieldGroup>

      <Button
        class="mt-6 self-start"
        :disabled="!canRun || running"
        @click="requestRun"
      >
        <Spinner v-if="running" />
        {{ $t("pages.toolJob.run") }}
      </Button>

      <div v-if="table" class="mt-8">
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
    <AlertDialog v-model:open="confirmOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmTitle }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ confirmDescription }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            :variant="entry?.destructive ? 'destructive' : 'default'"
            :disabled="running"
            @click="run"
          >
            <Spinner v-if="running" class="mr-2" />
            {{ $t("pages.toolsPage.quickRunConfirm") }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </AppPageChrome>
</template>
