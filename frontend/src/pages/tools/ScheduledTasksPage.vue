<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import { EmptyState } from "@/components/ui/empty-state";
import { Spinner } from "@/components/ui/spinner";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import { http } from "@/main";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { Clock3, History, Pencil, Plus, Trash2 } from "lucide-vue-next";

const blankDraft = () => ({
  name: "",
  actionId: "",
  intervalMinutes: 60,
  timeZoneId: "UTC",
  isEnabled: true,
  parametersText: "{}",
  connectionId: "",
  maxProducts: 50,
  targetCategoryId: "",
});

export default {
  components: {
    AppPageChrome,
    Button,
    Input,
    Textarea,
    Switch,
    EmptyState,
    Spinner,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    FieldGroup,
    SettingsFormField,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
    ConfirmDialog,
    Clock3,
    History,
    Pencil,
    Plus,
    Trash2,
  },
  data() {
    return {
      loading: true,
      saving: false,
      tasks: [],
      actions: [],
      timezones: [],
      connections: [],
      runsByTask: {},
      loadingRunsFor: null,
      expandedTaskId: null,
      dialogOpen: false,
      draft: blankDraft(),
      editingId: null,
    };
  },
  computed: {
    canCreate() {
      return this.actions.length > 0 && !this.saving;
    },
    currentActionAvailable() {
      return this.actions.some((action) => action.id === this.draft.actionId);
    },
    timezoneOptions() {
      const options = new Map();
      for (const timezone of this.timezones) {
        const value = timezone.value || timezone.id;
        if (!value) continue;
        options.set(value, { value, label: timezone.label || value });
      }
      if (this.draft.timeZoneId && !options.has(this.draft.timeZoneId)) {
        options.set(this.draft.timeZoneId, {
          value: this.draft.timeZoneId,
          label: this.draft.timeZoneId,
        });
      }
      return [...options.values()];
    },
  },
  mounted() {
    this.load();
  },
  methods: {
    async load() {
      this.loading = true;
      try {
        const [taskResponse, actionResponse, timezoneResponse] =
          await Promise.all([
            http.get("/scheduled-tasks", { cache: false }),
            http.get("/scheduled-tasks/actions", { cache: false }),
            http
              .get("/reference/timezones", { cache: false })
              .catch(() => null),
          ]);
        const tasks = extractResponseData({ data: taskResponse.data }, []);
        const actions = extractResponseData({ data: actionResponse.data }, []);
        this.tasks = Array.isArray(tasks) ? tasks : [];
        this.actions = Array.isArray(actions) ? actions : [];
        const timezones = timezoneResponse
          ? extractResponseData({ data: timezoneResponse.data }, [])
          : [];
        this.timezones = Array.isArray(timezones) ? timezones : [];
        this.connections = [];
      } catch (error) {
        displaySonnerError(error);
        this.tasks = [];
        this.actions = [];
        this.timezones = [];
        this.connections = [];
      } finally {
        this.loading = false;
      }
    },
    actionName(id) {
      const key = {
        "export-prices": "exportPrices",
        "export-stocks": "exportStocks",
        "import-prices": "importPrices",
        "import-products": "importProducts",
        "verify-product-links": "verifyProductLinks",
        "check-export-readiness": "checkExportReadiness",
        "compare-prices": "comparePrices",
      }[id];
      if (key) return this.$t(`pages.scheduledTasks.actions.${key}`);
      return (
        this.actions.find((action) => action.id === id)?.name ||
        this.$t("pages.scheduledTasks.unknownAction", { id })
      );
    },
    actionDescription(id) {
      const key = {
        "export-prices": "exportPricesDescription",
        "export-stocks": "exportStocksDescription",
        "import-prices": "importPricesDescription",
        "import-products": "importProductsDescription",
        "verify-product-links": "verifyProductLinksDescription",
        "check-export-readiness": "checkExportReadinessDescription",
        "compare-prices": "comparePricesDescription",
      }[id];
      return key ? this.$t(`pages.scheduledTasks.actions.${key}`) : "";
    },
    formatDate(value, timeZoneId = "UTC") {
      if (!value) return "—";
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) return "—";
      const options = { dateStyle: "medium", timeStyle: "short" };
      try {
        return new Intl.DateTimeFormat(this.$i18n?.locale || undefined, {
          ...options,
          timeZone: timeZoneId || "UTC",
        }).format(date);
      } catch {
        return new Intl.DateTimeFormat(
          this.$i18n?.locale || undefined,
          options,
        ).format(date);
      }
    },
    isBuiltInAction(id) {
      return [
        "export-prices",
        "export-stocks",
        "import-prices",
        "import-products",
        "verify-product-links",
        "check-export-readiness",
        "compare-prices",
      ].includes(id);
    },
    async loadActionConnections(actionId) {
      this.connections = [];
      if (!this.isBuiltInAction(actionId)) return;
      try {
        const response = await http.get(
          `/scheduled-tasks/connections?actionId=${encodeURIComponent(actionId)}`,
          { cache: false },
        );
        const connections = extractResponseData({ data: response.data }, []);
        this.connections = Array.isArray(connections) ? connections : [];
      } catch (error) {
        displaySonnerError(error);
      }
    },
    async startCreate() {
      this.editingId = null;
      this.draft = { ...blankDraft(), actionId: this.actions[0]?.id || "" };
      await this.loadActionConnections(this.draft.actionId);
      this.dialogOpen = true;
    },
    async startEdit(task) {
      this.editingId = task.id;
      this.draft = {
        name: task.name || "",
        actionId: task.actionId || "",
        intervalMinutes: task.intervalMinutes || 60,
        timeZoneId: task.timeZoneId || "UTC",
        isEnabled: task.isEnabled !== false,
        parametersText: JSON.stringify(task.parameters || {}, null, 2),
        connectionId:
          task.parameters?.connectionId != null
            ? String(task.parameters.connectionId)
            : "",
        maxProducts: task.parameters?.maxProducts ?? 50,
        targetCategoryId:
          task.parameters?.targetCategoryId != null
            ? String(task.parameters.targetCategoryId)
            : "",
      };
      await this.loadActionConnections(this.draft.actionId);
      this.dialogOpen = true;
    },
    async onActionChanged() {
      this.draft.connectionId = "";
      await this.loadActionConnections(this.draft.actionId);
    },
    async save() {
      let parameters;
      if (this.isBuiltInAction(this.draft.actionId)) {
        const connectionId = Number(this.draft.connectionId);
        if (!Number.isInteger(connectionId) || connectionId <= 0) {
          displaySonnerError(
            this.$t("pages.scheduledTasks.connectionRequired"),
          );
          return;
        }
        parameters = { connectionId };
        if (this.draft.actionId === "import-products") {
          parameters.maxProducts = Number(this.draft.maxProducts);
          if (this.draft.targetCategoryId) {
            parameters.targetCategoryId = Number(this.draft.targetCategoryId);
          }
        }
      } else {
        try {
          parameters = JSON.parse(this.draft.parametersText || "{}");
          if (
            !parameters ||
            Array.isArray(parameters) ||
            typeof parameters !== "object"
          )
            throw new Error("not_object");
        } catch {
          displaySonnerError(this.$t("pages.scheduledTasks.invalidParameters"));
          return;
        }
      }

      const payload = {
        name: this.draft.name.trim(),
        actionId: this.draft.actionId,
        parameters,
        intervalMinutes: Number(this.draft.intervalMinutes),
        timeZoneId: this.draft.timeZoneId || "UTC",
        isEnabled: Boolean(this.draft.isEnabled),
      };
      this.saving = true;
      try {
        if (this.editingId) {
          await http.put(`/scheduled-tasks/${this.editingId}`, payload, {
            cache: false,
          });
        } else {
          await http.post("/scheduled-tasks", payload, { cache: false });
        }
        this.dialogOpen = false;
        displaySonnerSuccess(this.$t("pages.scheduledTasks.saved"));
        await this.load();
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.saving = false;
      }
    },
    async setEnabled(task, enabled) {
      try {
        const response = await http.put(
          `/scheduled-tasks/${task.id}`,
          {
            name: task.name,
            actionId: task.actionId,
            parameters: task.parameters || {},
            intervalMinutes: task.intervalMinutes,
            timeZoneId: task.timeZoneId || "UTC",
            isEnabled: enabled,
          },
          { cache: false },
        );
        const updated = extractResponseData({ data: response.data }, null);
        task.isEnabled = enabled;
        if (updated?.nextRunAt) task.nextRunAt = updated.nextRunAt;
      } catch (error) {
        displaySonnerError(error);
        await this.load();
      }
    },
    async deleteTask(id) {
      try {
        await http.delete(`/scheduled-tasks/${id}`, { cache: false });
        this.tasks = this.tasks.filter((task) => task.id !== id);
        displaySonnerSuccess(this.$t("common.delete"));
      } catch (error) {
        displaySonnerError(error);
      }
    },
    async toggleHistory(task) {
      if (this.expandedTaskId === task.id) {
        this.expandedTaskId = null;
        return;
      }
      this.expandedTaskId = task.id;
      if (this.runsByTask[task.id]) return;
      this.loadingRunsFor = task.id;
      try {
        const response = await http.get(`/scheduled-tasks/${task.id}/runs`, {
          cache: false,
        });
        const runs = extractResponseData({ data: response.data }, []);
        this.runsByTask = {
          ...this.runsByTask,
          [task.id]: Array.isArray(runs) ? runs : [],
        };
      } catch (error) {
        displaySonnerError(error);
        this.expandedTaskId = null;
      } finally {
        this.loadingRunsFor = null;
      }
    },
    runStatus(status) {
      const key =
        status === "succeeded"
          ? "runSucceeded"
          : status === "running"
            ? "runRunning"
            : "runFailed";
      return this.$t(`pages.scheduledTasks.${key}`);
    },
  },
};
</script>

<template>
  <AppPageChrome>
    <EmptyState v-if="loading" loading class="min-h-0 flex-1" />
    <div v-else class="page-region-in flex w-full max-w-xl flex-col">
      <div class="mb-6 flex flex-wrap items-center justify-start gap-2">
        <Button :disabled="!canCreate" @click="startCreate">
          <Plus class="mr-2 size-4" />
          {{ $t("pages.scheduledTasks.create") }}
        </Button>
      </div>

      <p class="mb-6 text-sm text-muted-foreground">
        {{ $t("pages.scheduledTasks.description") }}
      </p>

      <div
        v-if="actions.length === 0"
        class="mb-4 rounded-md border bg-muted/40 px-3 py-2.5 text-sm"
        role="status"
      >
        <p class="font-medium">{{ $t("pages.scheduledTasks.noActions") }}</p>
        <p class="mt-1 text-muted-foreground">
          {{ $t("pages.scheduledTasks.noActionsHint") }}
        </p>
      </div>

      <EmptyState
        v-if="tasks.length === 0"
        :title="$t('pages.scheduledTasks.emptyTitle')"
        :description="$t('pages.scheduledTasks.emptyDescription')"
        class="min-h-0 flex-1"
      >
        <template #icon><Clock3 /></template>
      </EmptyState>

      <section
        v-else
        :aria-label="$t('routes.scheduledTasks')"
        class="flex flex-col gap-2"
      >
        <InsetGroup>
          <InsetGroupItem
            v-for="task in tasks"
            :key="task.id"
            class="items-start py-3"
          >
            <template #leading>
              <span
                class="flex size-8 items-center justify-center rounded-full bg-muted text-muted-foreground"
              >
                <Clock3 class="size-4" />
              </span>
            </template>
            <div class="min-w-0">
              <p class="truncate font-medium">{{ task.name }}</p>
              <p class="mt-0.5 truncate text-xs text-muted-foreground">
                {{ actionName(task.actionId) }} ·
                {{
                  $t("pages.scheduledTasks.intervalDisplay", {
                    minutes: task.intervalMinutes,
                  })
                }}
              </p>
              <p class="mt-1 text-xs text-muted-foreground">
                {{ $t("pages.scheduledTasks.nextRun") }}:
                {{ formatDate(task.nextRunAt, task.timeZoneId) }}
              </p>
              <p class="mt-1 text-xs text-muted-foreground">
                {{ $t("pages.scheduledTasks.timeZone") }}:
                {{ task.timeZoneId || "UTC" }}
              </p>
            </div>
            <template #trailing>
              <div
                class="flex flex-wrap items-center justify-end gap-1"
                @click.stop
              >
                <span
                  class="mr-1 hidden text-xs text-muted-foreground sm:inline"
                >
                  {{
                    task.isEnabled
                      ? $t("pages.scheduledTasks.statusEnabled")
                      : $t("pages.scheduledTasks.statusDisabled")
                  }}
                </span>
                <Switch
                  :model-value="task.isEnabled"
                  :aria-label="
                    task.isEnabled
                      ? $t('pages.scheduledTasks.statusEnabled')
                      : $t('pages.scheduledTasks.statusDisabled')
                  "
                  @update:model-value="setEnabled(task, $event)"
                />
                <Button
                  variant="ghost"
                  size="icon"
                  class="size-8"
                  :aria-label="$t('common.edit')"
                  @click="startEdit(task)"
                >
                  <Pencil class="size-4" />
                </Button>
                <ConfirmDialog
                  :title="$t('pages.scheduledTasks.deleteTitle')"
                  :description="$t('pages.scheduledTasks.deleteDescription')"
                  :confirm-label="$t('common.delete')"
                  @confirm="deleteTask(task.id)"
                >
                  <template #trigger>
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                      :aria-label="$t('common.delete')"
                      @click.stop
                    >
                      <Trash2 class="size-4" />
                    </Button>
                  </template>
                </ConfirmDialog>
                <Button
                  variant="ghost"
                  size="icon"
                  class="size-8"
                  :aria-label="$t('pages.scheduledTasks.history')"
                  :aria-expanded="expandedTaskId === task.id"
                  @click="toggleHistory(task)"
                >
                  <History class="size-4" />
                </Button>
              </div>
            </template>
          </InsetGroupItem>
        </InsetGroup>

        <div
          v-if="expandedTaskId"
          class="rounded-md border bg-muted/20 px-3 py-3"
        >
          <h2 class="mb-3 text-sm font-medium">
            {{ $t("pages.scheduledTasks.history") }}
          </h2>
          <div
            v-if="loadingRunsFor === expandedTaskId"
            class="flex justify-center py-4"
          >
            <Spinner />
          </div>
          <p
            v-else-if="!(runsByTask[expandedTaskId] || []).length"
            class="text-sm text-muted-foreground"
          >
            {{ $t("pages.scheduledTasks.noRuns") }}
          </p>
          <ul v-else class="divide-y">
            <li
              v-for="run in runsByTask[expandedTaskId]"
              :key="run.id"
              class="flex flex-wrap items-center justify-between gap-2 py-2 text-sm"
            >
              <span class="text-muted-foreground">{{
                formatDate(run.startedAt)
              }}</span>
              <span
                :class="
                  run.status === 'succeeded'
                    ? 'text-success'
                    : run.status === 'failed'
                      ? 'text-destructive'
                      : 'text-foreground'
                "
              >
                {{ runStatus(run.status) }}
              </span>
              <span
                v-if="run.error"
                class="w-full break-words text-xs text-destructive"
                >{{ run.error }}</span
              >
            </li>
          </ul>
        </div>
      </section>

      <Dialog v-model:open="dialogOpen">
        <DialogContent
          size="lg"
          class="grid max-h-[min(44rem,calc(100dvh-5rem))] grid-rows-[auto_minmax(0,1fr)] gap-0 overflow-hidden p-0"
        >
          <DialogHeader variant="bar">
            <DialogTitle>
              {{
                $t(
                  editingId
                    ? "pages.scheduledTasks.edit"
                    : "pages.scheduledTasks.create",
                )
              }}
            </DialogTitle>
            <DialogDescription>{{
              $t("pages.scheduledTasks.description")
            }}</DialogDescription>
          </DialogHeader>
          <form
            class="grid min-h-0 grid-rows-[minmax(0,1fr)_auto]"
            @submit.prevent="save"
          >
            <div class="min-h-0 overflow-y-auto overscroll-contain px-6 py-5">
              <div class="space-y-5">
                <section>
                  <InsetGroupLabel>{{
                    $t("pages.scheduledTasks.groupTask")
                  }}</InsetGroupLabel>
                  <InsetGroup>
                    <InsetGroupItem
                      as="div"
                      class="cursor-default items-stretch px-3 py-3 hover:bg-transparent"
                    >
                      <FieldGroup class="gap-4">
                        <SettingsFormField
                          :label="$t('pages.scheduledTasks.name')"
                          label-for="scheduled-task-name"
                          :required="true"
                        >
                          <Input
                            id="scheduled-task-name"
                            v-model="draft.name"
                            required
                            maxlength="120"
                            :placeholder="
                              $t('pages.scheduledTasks.namePlaceholder')
                            "
                          />
                        </SettingsFormField>
                        <SettingsFormField
                          :label="$t('pages.scheduledTasks.action')"
                          label-for="scheduled-task-action"
                          :description="actionDescription(draft.actionId)"
                          :required="true"
                        >
                          <Select
                            v-model="draft.actionId"
                            @update:model-value="onActionChanged"
                          >
                            <SelectTrigger id="scheduled-task-action">
                              <SelectValue
                                :placeholder="
                                  $t('pages.scheduledTasks.actionPlaceholder')
                                "
                              />
                            </SelectTrigger>
                            <SelectContent>
                              <SelectItem
                                v-if="!currentActionAvailable && draft.actionId"
                                :value="draft.actionId"
                              >
                                {{
                                  $t("pages.scheduledTasks.unknownAction", {
                                    id: draft.actionId,
                                  })
                                }}
                              </SelectItem>
                              <SelectItem
                                v-for="action in actions"
                                :key="action.id"
                                :value="action.id"
                              >
                                {{ actionName(action.id) }}
                              </SelectItem>
                            </SelectContent>
                          </Select>
                        </SettingsFormField>
                      </FieldGroup>
                    </InsetGroupItem>
                  </InsetGroup>
                </section>

                <section v-if="isBuiltInAction(draft.actionId)">
                  <InsetGroupLabel>{{
                    $t("pages.scheduledTasks.groupConnection")
                  }}</InsetGroupLabel>
                  <InsetGroup>
                    <InsetGroupItem
                      as="div"
                      class="cursor-default items-stretch px-3 py-3 hover:bg-transparent"
                    >
                      <SettingsFormField
                        :label="$t('pages.scheduledTasks.connection')"
                        label-for="scheduled-task-connection"
                        :description="
                          connections.length === 0
                            ? $t('pages.scheduledTasks.noActiveConnections')
                            : ''
                        "
                        :required="true"
                      >
                        <Select
                          v-model="draft.connectionId"
                          :disabled="connections.length === 0"
                        >
                          <SelectTrigger id="scheduled-task-connection">
                            <SelectValue
                              :placeholder="
                                $t('pages.scheduledTasks.connectionPlaceholder')
                              "
                            />
                          </SelectTrigger>
                          <SelectContent>
                            <SelectItem
                              v-for="connection in connections"
                              :key="connection.id"
                              :value="String(connection.id)"
                            >
                              {{ connection.name }}
                            </SelectItem>
                          </SelectContent>
                        </Select>
                      </SettingsFormField>
                    </InsetGroupItem>
                  </InsetGroup>
                </section>

                <section v-if="draft.actionId === 'import-products'">
                  <InsetGroupLabel>{{
                    $t("pages.scheduledTasks.groupRunOptions")
                  }}</InsetGroupLabel>
                  <InsetGroup>
                    <InsetGroupItem
                      as="div"
                      class="cursor-default items-stretch px-3 py-3 hover:bg-transparent"
                    >
                      <FieldGroup class="gap-4">
                        <SettingsFormField
                          :label="$t('pages.scheduledTasks.maxProducts')"
                          label-for="scheduled-task-max-products"
                          :description="
                            $t('pages.scheduledTasks.maxProductsHint')
                          "
                          :required="true"
                        >
                          <Input
                            id="scheduled-task-max-products"
                            v-model.number="draft.maxProducts"
                            type="number"
                            min="1"
                            max="200"
                            required
                            class="max-w-36 tabular-nums"
                          />
                        </SettingsFormField>
                        <SettingsFormField
                          :label="$t('pages.scheduledTasks.categoryIdLabel')"
                          label-for="scheduled-task-target-category"
                          :description="
                            $t('pages.scheduledTasks.categoryIdHint')
                          "
                        >
                          <Input
                            id="scheduled-task-target-category"
                            v-model="draft.targetCategoryId"
                            type="number"
                            min="1"
                            class="max-w-56 tabular-nums"
                          />
                        </SettingsFormField>
                      </FieldGroup>
                    </InsetGroupItem>
                  </InsetGroup>
                </section>

                <section v-if="!isBuiltInAction(draft.actionId)">
                  <InsetGroupLabel>{{
                    $t("pages.scheduledTasks.groupRunOptions")
                  }}</InsetGroupLabel>
                  <InsetGroup>
                    <InsetGroupItem
                      as="div"
                      class="cursor-default items-stretch px-3 py-3 hover:bg-transparent"
                    >
                      <SettingsFormField
                        :label="$t('pages.scheduledTasks.parameters')"
                        label-for="scheduled-task-parameters"
                        :description="$t('pages.scheduledTasks.parametersHint')"
                      >
                        <Textarea
                          id="scheduled-task-parameters"
                          v-model="draft.parametersText"
                          rows="5"
                          spellcheck="false"
                          class="font-mono text-xs"
                        />
                      </SettingsFormField>
                    </InsetGroupItem>
                  </InsetGroup>
                </section>

                <section>
                  <InsetGroupLabel>{{
                    $t("pages.scheduledTasks.groupInterval")
                  }}</InsetGroupLabel>
                  <InsetGroup>
                    <InsetGroupItem
                      as="div"
                      class="cursor-default items-stretch px-3 py-3 hover:bg-transparent"
                    >
                      <FieldGroup class="gap-4">
                        <SettingsFormField
                          :label="$t('pages.scheduledTasks.interval')"
                          label-for="scheduled-task-interval"
                          :required="true"
                        >
                          <div class="flex items-center gap-2">
                            <Input
                              id="scheduled-task-interval"
                              v-model.number="draft.intervalMinutes"
                              type="number"
                              min="1"
                              max="525600"
                              required
                              class="max-w-36 tabular-nums"
                            />
                            <span class="text-sm text-muted-foreground">{{
                              $t("pages.scheduledTasks.minutes")
                            }}</span>
                          </div>
                        </SettingsFormField>
                        <SettingsFormField
                          :label="$t('pages.scheduledTasks.timeZone')"
                          label-for="scheduled-task-timezone"
                          :required="true"
                        >
                          <Select v-model="draft.timeZoneId">
                            <SelectTrigger
                              id="scheduled-task-timezone"
                              class="w-full"
                            >
                              <SelectValue
                                :placeholder="
                                  $t('pages.scheduledTasks.timeZone')
                                "
                              />
                            </SelectTrigger>
                            <SelectContent>
                              <SelectItem
                                v-for="timezone in timezoneOptions"
                                :key="timezone.value"
                                :value="timezone.value"
                              >
                                {{ timezone.label }}
                              </SelectItem>
                            </SelectContent>
                          </Select>
                        </SettingsFormField>
                        <SettingsFormField
                          :label="$t('pages.scheduledTasks.enabled')"
                          label-for="scheduled-task-enabled"
                          inline
                        >
                          <Switch
                            id="scheduled-task-enabled"
                            v-model:model-value="draft.isEnabled"
                          />
                        </SettingsFormField>
                      </FieldGroup>
                    </InsetGroupItem>
                  </InsetGroup>
                </section>
              </div>
            </div>
            <DialogFooter variant="bar" class="sm:justify-end">
              <Button
                type="button"
                variant="outline"
                :disabled="saving"
                @click="dialogOpen = false"
              >
                {{ $t("common.cancel") }}
              </Button>
              <Button
                type="submit"
                :disabled="
                  saving ||
                  !draft.actionId ||
                  (isBuiltInAction(draft.actionId) && !draft.connectionId)
                "
              >
                <Spinner v-if="saving" class="mr-2 size-4" />
                {{ $t("common.save") }}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  </AppPageChrome>
</template>
