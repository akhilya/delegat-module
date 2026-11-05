<script>
import { Spinner } from "@/components/ui/spinner";
import PageSection from "@/components/pageSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
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
} from "@/components/ui/alert-dialog";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Switch } from "@/components/ui/switch";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Cable, Pencil, Plus, Save, Trash2, Zap } from "lucide-vue-next";
import { useStore } from "vuex";
import { ref, onMounted } from "vue";
import { t } from "@/i18n";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";

export default {
  components: {
    Spinner,
    PageSection,
    SettingsFormField,
    FieldGroup,
    Button,
    Input,
    PasswordInput,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogFooter,
    DialogHeader,
    DialogTitle,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Switch,
    Pencil,
    Plus,
    Save,
    Trash2,
    Zap,
  },
  setup() {
    const store = useStore();
    const servers = ref([]);
    const isLoading = ref(false);
    const isDialogOpen = ref(false);
    const editing = ref(null);
    const isSaving = ref(false);
    const testingId = ref(null);
    const form = ref({ name: "", url: "", apiKey: "" });
    const confirmDialog = ref({
      open: false,
      title: "",
      message: "",
      resolve: null,
    });

    function showConfirm(title, message) {
      return new Promise((r) => {
        confirmDialog.value = { open: true, title, message, resolve: r };
      });
    }

    async function load() {
      isLoading.value = true;
      try {
        await store.dispatch("aiModels/getMcpServers", {
          onSuccess: (r) => {
            servers.value = r.data.value ?? [];
          },
          onError: displaySonnerError,
        });
      } finally {
        isLoading.value = false;
      }
    }

    function openCreate() {
      editing.value = null;
      form.value = { name: "", url: "", apiKey: "" };
      isDialogOpen.value = true;
    }

    function openEdit(row) {
      editing.value = row;
      form.value = { name: row.name, url: row.url, apiKey: "" };
      isDialogOpen.value = true;
    }

    function closeDialog() {
      isDialogOpen.value = false;
      editing.value = null;
    }

    async function save() {
      if (!form.value.name?.trim() || !form.value.url?.trim()) {
        displaySonnerError(t("pages.aiModelsPage.mcpFillRequiredError"));
        return;
      }
      if (
        fieldLimitIssue(form.value.name, FIELD_LIMITS.aiModelName) ===
          "tooLong" ||
        fieldLimitIssue(form.value.url, FIELD_LIMITS.aiModelUrl) === "tooLong"
      ) {
        displaySonnerError(
          t("common.tooLong", { max: FIELD_LIMITS.aiModelUrl }),
        );
        return;
      }
      isSaving.value = true;
      try {
        const data = {
          name: form.value.name.trim(),
          url: form.value.url.trim(),
        };
        if (form.value.apiKey) data.apiKey = form.value.apiKey;
        if (editing.value) {
          await store.dispatch("aiModels/updateMcpServer", {
            id: editing.value.id,
            data,
            onSuccess: () => {},
            onError: (e) => {
              throw e;
            },
          });
        } else {
          await store.dispatch("aiModels/createMcpServer", {
            data,
            onSuccess: () => {},
            onError: (e) => {
              throw e;
            },
          });
        }
        displaySonnerSuccess(
          editing.value
            ? t("pages.aiModelsPage.mcpUpdated")
            : t("pages.aiModelsPage.mcpAdded"),
        );
        closeDialog();
        await load();
      } catch (e) {
        displaySonnerError(e);
      } finally {
        isSaving.value = false;
      }
    }

    async function deleteServer(row) {
      const confirmed = await showConfirm(
        t("pages.aiModelsPage.deleteMcpTitle"),
        t("pages.aiModelsPage.deleteMcpDescription", { name: row.name }),
      );
      if (!confirmed) return;
      await store.dispatch("aiModels/deleteMcpServer", {
        id: row.id,
        onSuccess: () => {
          displaySonnerSuccess(t("pages.aiModelsPage.mcpDeleted"));
          load();
        },
        onError: displaySonnerError,
      });
    }

    async function setEnabled(row, enabled) {
      if (row.isEnabled === enabled) return;
      const prev = row.isEnabled;
      row.isEnabled = enabled;
      try {
        await store.dispatch("aiModels/toggleMcpServerEnabled", { id: row.id });
        await load();
      } catch {
        row.isEnabled = prev;
        displaySonnerError(t("pages.aiModelsPage.toggleMcpEnabledError"));
      }
    }

    async function testServer(row) {
      testingId.value = row.id;
      await store.dispatch("aiModels/testMcpServer", {
        id: row.id,
        onSuccess: (r) => {
          if (r.data.value?.success) displaySonnerSuccess(r.data.value.message);
          else
            displaySonnerError(
              new Error(
                r.data.value?.message ??
                  t("pages.aiModelsPage.connectionError"),
              ),
            );
        },
        onError: displaySonnerError,
      });
      testingId.value = null;
    }

    onMounted(load);

    return {
      emptyListIcon: Cable,
      servers,
      isLoading,
      isDialogOpen,
      editing,
      isSaving,
      testingId,
      form,
      confirmDialog,
      FIELD_LIMITS,
      openCreate,
      openEdit,
      closeDialog,
      save,
      deleteServer,
      setEnabled,
      testServer,
    };
  },
};
</script>

<template>
  <div class="flex min-h-0 flex-col">
    <div class="mb-3 flex flex-wrap items-end justify-between gap-3">
      <div class="min-w-0">
        <h2 class="text-sm font-medium">
          {{ $t("pages.aiModelsPage.mcpSectionTitle") }}
        </h2>
        <p class="mt-0.5 text-xs text-muted-foreground">
          {{ $t("pages.aiModelsPage.mcpSectionDescription") }}
        </p>
      </div>
      <Button size="sm" @click="openCreate">
        <Plus class="mr-2 size-4" />
        {{ $t("pages.aiModelsPage.addMcp") }}
      </Button>
    </div>

    <PageSection
      class="!flex-none"
      :loading="isLoading"
      :empty="!isLoading && servers.length === 0"
      :empty-icon="emptyListIcon"
      :empty-title="$t('pages.aiModelsPage.emptyMcpTitle')"
      :empty-description="$t('pages.aiModelsPage.emptyMcpDescription')"
    >
      <template #empty-action>
        <Button class="mt-2" @click="openCreate">
          <Plus class="mr-2 size-4" />
          {{ $t("pages.aiModelsPage.addMcp") }}
        </Button>
      </template>

      <div class="overflow-x-auto">
        <Table class="table-fixed">
          <TableHeader>
            <TableRow>
              <TableHead class="w-28">{{ $t("common.status") }}</TableHead>
              <TableHead class="w-[22%]">{{ $t("common.name") }}</TableHead>
              <TableHead>URL</TableHead>
              <TableHead class="w-36"></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="row in servers" :key="row.id">
              <TableCell>
                <Switch
                  :model-value="row.isEnabled"
                  class="scale-[0.8]"
                  @update:model-value="(v) => setEnabled(row, v)"
                />
              </TableCell>
              <TableCell class="max-w-0">
                <div class="truncate font-medium" :title="row.name">
                  {{ row.name }}
                </div>
              </TableCell>
              <TableCell class="max-w-0 text-muted-foreground text-sm">
                <div class="truncate" :title="row.url">{{ row.url }}</div>
              </TableCell>
              <TableCell>
                <div class="flex items-center justify-end gap-1">
                  <Button
                    variant="ghost"
                    size="sm"
                    :disabled="testingId === row.id"
                    @click="testServer(row)"
                  >
                    <Spinner
                      v-if="testingId === row.id"
                      size="xs"
                      class="mr-1"
                    />
                    <Zap v-else class="mr-1 size-3" />
                    {{ $t("pages.aiModelsPage.testButton") }}
                  </Button>
                  <Button variant="ghost" size="icon" @click="openEdit(row)">
                    <Pencil class="size-4" />
                    <span class="sr-only">{{ $t("common.edit") }}</span>
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    reveal-on-row-hover
                    class="text-destructive hover:bg-destructive/10 hover:text-destructive"
                    @click="deleteServer(row)"
                  >
                    <Trash2 class="size-4" />
                    <span class="sr-only">{{ $t("common.delete") }}</span>
                  </Button>
                </div>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </PageSection>

    <AlertDialog
      :open="confirmDialog.open"
      @update:open="
        (v) => {
          if (!v) confirmDialog.resolve?.(false);
          confirmDialog.open = v;
        }
      "
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmDialog.title }}</AlertDialogTitle>
          <AlertDialogDescription>{{
            confirmDialog.message
          }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel
            @click="
              confirmDialog.resolve(false);
              confirmDialog.open = false;
            "
            >{{ $t("common.cancel") }}</AlertDialogCancel
          >
          <AlertDialogAction
            variant="destructive"
            @click="
              confirmDialog.resolve(true);
              confirmDialog.open = false;
            "
            >{{ $t("common.delete") }}</AlertDialogAction
          >
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="isDialogOpen">
      <DialogContent
        size="lg"
        class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
      >
        <DialogHeader variant="bar">
          <DialogTitle>{{
            editing
              ? $t("pages.aiModelsPage.editMcpTitle")
              : $t("pages.aiModelsPage.addMcpDialogTitle")
          }}</DialogTitle>
          <DialogDescription>
            {{
              editing
                ? $t("pages.aiModelsPage.editMcpDescription")
                : $t("pages.aiModelsPage.addMcpDescription")
            }}
          </DialogDescription>
        </DialogHeader>

        <form
          id="ai-mcp-form"
          class="min-h-0 flex-1 overflow-y-auto px-6 py-5"
          @submit.prevent="save"
        >
          <FieldGroup class="gap-5">
            <SettingsFormField :label="$t('common.name')">
              <Input
                v-model="form.name"
                :maxlength="FIELD_LIMITS.aiModelName"
              />
            </SettingsFormField>
            <SettingsFormField :label="$t('pages.aiModelsPage.mcpUrlLabel')">
              <Input
                v-model="form.url"
                :maxlength="FIELD_LIMITS.aiModelUrl"
                :placeholder="$t('pages.aiModelsPage.mcpUrlPlaceholder')"
              />
              <template #description>
                {{ $t("pages.aiModelsPage.mcpUrlHint") }}
              </template>
            </SettingsFormField>
            <SettingsFormField :label="$t('pages.aiModelsPage.apiKeyLabel')">
              <PasswordInput v-model="form.apiKey" autocomplete="off" />
              <template #description>
                {{
                  editing
                    ? $t("pages.aiModelsPage.mcpApiKeyHint")
                    : $t("pages.aiModelsPage.mcpApiKeyCreateHint")
                }}
              </template>
            </SettingsFormField>
          </FieldGroup>
        </form>

        <DialogFooter variant="bar">
          <Button type="button" variant="outline" @click="closeDialog">{{
            $t("common.cancel")
          }}</Button>
          <Button type="submit" form="ai-mcp-form" :disabled="isSaving">
            <Spinner v-if="isSaving" class="mr-2" />
            <Save v-else class="mr-2 size-4" />
            {{ isSaving ? $t("pages.aiModelsPage.saving") : $t("common.save") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
