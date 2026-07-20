<script>
import PageSection from "@/components/pageSection.vue";
import SettingsFormSection from "@/components/settings/SettingsFormSection.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { EmptyState } from "@/components/ui/empty-state";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import PasswordInput from "@/components/ui/input/PasswordInput.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PageHeader } from "@/components/ui/page-header";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { Textarea } from "@/components/ui/textarea";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Switch } from "@/components/ui/switch";
import { Bot, CheckCircle2, Loader2, Pencil, Plus, RefreshCw, Save, Trash2, Zap, Network } from "lucide-vue-next";
import { useStore } from "vuex";
import { ref, computed, onMounted } from "vue";
import { t } from "@/i18n";

export default {
  components: {
    PageSection,
    SettingsFormSection,
    SettingsFormField,
    EmptyState,
    Button, Input, PasswordInput, Label, Badge, Textarea, Switch,
    Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
    AlertDialog, AlertDialogAction, AlertDialogCancel,
    AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
    AlertDialogHeader, AlertDialogTitle,
    Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
    PageHeader,
    Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
    Bot, CheckCircle2, Loader2, Pencil, Plus, RefreshCw, Save, Trash2, Zap, Network,
  },
  setup() {
    const store = useStore();
    const models = ref([]);
    const isLoading = ref(false);
    const isDialogOpen = ref(false);
    const editingModel = ref(null);
    const isSaving = ref(false);
    const testingId = ref(null);
    const isSettingsSaving = ref(false);
    const settingsForm = ref({
      storeDescription: "",
      communicationStyle: "professional",
      responseBrevity: "balanced",
      emojiUsage: "none",
    });

    const defaultForm = () => ({
      name: "",
      apiUrl: "https://api.anthropic.com/v1/messages",
      apiKey: "",
      modelId: "claude-sonnet-4-6",
      provider: "0",
      proxyUrl: "",
    });
    const form = ref(defaultForm());
    const modelOptions = ref([]);
    const isDiscovering = ref(false);
    const confirmDialog = ref({ open: false, title: "", message: "", resolve: null });

    function showConfirm(title, message) {
      return new Promise((r) => { confirmDialog.value = { open: true, title, message, resolve: r }; });
    }

    async function discoverModels() {
      if (!form.value.apiUrl) {
        displaySonnerError(t("pages.aiModelsPage.apiUrlRequiredError"));
        return;
      }
      isDiscovering.value = true;
      try {
        await store.dispatch("aiModels/discoverModels", {
          data: {
            apiUrl: form.value.apiUrl,
            apiKey: form.value.apiKey || null,
            provider: Number(form.value.provider),
            modelConfigId: editingModel.value?.id ?? null,
            proxyUrl: form.value.proxyUrl || null,
          },
          onSuccess: (r) => {
            const list = r.data.value?.models ?? [];
            if (list.length === 0) {
              displaySonnerError(new Error(r.data.value?.error || t("pages.aiModelsPage.modelsNotFoundError")));
              return;
            }
            modelOptions.value = list;
            if (!list.includes(form.value.modelId)) form.value.modelId = list[0];
          },
          onError: (e) => { displaySonnerError(e); },
        });
      } finally {
        isDiscovering.value = false;
      }
    }

    async function load() {
      isLoading.value = true;
      try {
        await store.dispatch("aiModels/getAiModels", {
          onSuccess: (r) => { models.value = r.data.value ?? []; },
          onError: (e) => { displaySonnerError(e); },
        });
      } finally {
        isLoading.value = false;
      }
    }

    async function loadSettings() {
      await store.dispatch("aiModels/getStoreSettings", {
        onSuccess: (r) => {
          if (r.data.value) Object.assign(settingsForm.value, r.data.value);
        },
        onError: () => {},
      });
    }

    async function saveSettings() {
      isSettingsSaving.value = true;
      try {
        await store.dispatch("aiModels/saveStoreSettings", {
          data: settingsForm.value,
          onSuccess: () => { displaySonnerSuccess(t("pages.aiModelsPage.settingsSaved")); },
          onError: (e) => { throw e; },
        });
      } catch (e) {
        displaySonnerError(e);
      } finally {
        isSettingsSaving.value = false;
      }
    }

    function openCreate() {
      editingModel.value = null;
      form.value = defaultForm();
      modelOptions.value = [];
      isDialogOpen.value = true;
    }

    // Backend serializes the enum as a string ("Anthropic"/"OpenAiCompatible"),
    // older records may come as numbers — normalize to the select values "0"/"1"
    const providerToSelect = (p) => (p === 0 || p === "0" || p === "Anthropic" ? "0" : "1");
    const isAnthropic = (p) => providerToSelect(p) === "0";

    function openEdit(model) {
      editingModel.value = model;
      form.value = {
        name: model.name,
        apiUrl: model.apiUrl,
        apiKey: "",
        modelId: model.modelId,
        provider: providerToSelect(model.provider),
        proxyUrl: model.proxyUrl ?? "",
      };
      modelOptions.value = [];
      isDialogOpen.value = true;
    }

    function closeDialog() {
      isDialogOpen.value = false;
      editingModel.value = null;
    }

    async function save() {
      if (!form.value.name || !form.value.apiUrl || !form.value.modelId) {
        displaySonnerError(t("pages.aiModelsPage.fillRequiredFieldsError"));
        return;
      }
      if (!editingModel.value && !form.value.apiKey) {
        displaySonnerError(t("pages.aiModelsPage.apiKeyRequiredError"));
        return;
      }
      isSaving.value = true;
      try {
        const data = {
          ...form.value,
          provider: Number(form.value.provider),
        };
        if (editingModel.value && !data.apiKey) delete data.apiKey;

        if (editingModel.value) {
          await store.dispatch("aiModels/updateAiModel", {
            id: editingModel.value.id,
            data,
            onSuccess: () => {},
            onError: (e) => { throw e; },
          });
        } else {
          await store.dispatch("aiModels/createAiModel", {
            data,
            onSuccess: () => {},
            onError: (e) => { throw e; },
          });
        }
        displaySonnerSuccess(editingModel.value ? t("pages.aiModelsPage.modelUpdated") : t("pages.aiModelsPage.modelAdded"));
        closeDialog();
        await load();
      } catch (e) {
        displaySonnerError(e);
      } finally {
        isSaving.value = false;
      }
    }

    async function deleteModel(model) {
      const confirmed = await showConfirm(
        t("pages.aiModelsPage.deleteModelTitle"),
        t("pages.aiModelsPage.deleteModelDescription", { name: model.name }),
      );
      if (!confirmed) return;
      await store.dispatch("aiModels/deleteAiModel", {
        id: model.id,
        onSuccess: () => { displaySonnerSuccess(t("pages.aiModelsPage.modelDeleted")); load(); },
        onError: displaySonnerError,
      });
    }

    async function setDefault(model) {
      await store.dispatch("aiModels/setDefaultAiModel", {
        id: model.id,
        onSuccess: () => { displaySonnerSuccess(t("pages.aiModelsPage.defaultModelChanged")); load(); },
        onError: displaySonnerError,
      });
    }

    async function setModelEnabled(model, enabled) {
      if (model.isEnabled === enabled) return;
      const prev = model.isEnabled;
      model.isEnabled = enabled;
      try {
        await store.dispatch("aiModels/toggleAiModelEnabled", { id: model.id });
        await load();
        store.dispatch("aiModels/getPickerModels", { onError: () => {} });
      } catch {
        model.isEnabled = prev;
        displaySonnerError(t("pages.aiModelsPage.toggleEnabledError"));
      }
    }

    const defaultModelId = computed(() =>
      String(models.value.find((m) => m.isDefault)?.id ?? "")
    );

    async function onDefaultModelChange(val) {
      const model = models.value.find((m) => String(m.id) === val);
      if (model) await setDefault(model);
    }

    async function testModel(model) {
      testingId.value = model.id;
      await store.dispatch("aiModels/testAiModel", {
        id: model.id,
        onSuccess: (r) => {
          if (r.data.value?.success) displaySonnerSuccess(r.data.value.message);
          else displaySonnerError(new Error(r.data.value?.message ?? t("pages.aiModelsPage.connectionError")));
        },
        onError: displaySonnerError,
      });
      testingId.value = null;
    }

    onMounted(() => { load(); loadSettings(); });

    return {
      models, isLoading, isDialogOpen, editingModel, isSaving, testingId, form,
      openCreate, openEdit, closeDialog, save, deleteModel, setDefault, testModel,
      setModelEnabled, defaultModelId, onDefaultModelChange,
      settingsForm, isSettingsSaving, saveSettings,
      modelOptions, isDiscovering, discoverModels, isAnthropic,
      confirmDialog,
    };
  },
};
</script>

<template>
  <div class="flex flex-col">
    <PageHeader
      :title="$t('nav.ai')"
      :description="$t('pages.aiModelsPage.description')"
    >
      <Button @click="openCreate">
        <Plus class="mr-2 size-4" />
        {{ $t("pages.aiModelsPage.addModel") }}
      </Button>
    </PageHeader>

    <PageSection :loading="isLoading" class="!min-h-0">
      <SettingsFormSection
        :title="$t('pages.aiModelsPage.modelsSectionTitle')"
        :description="$t('pages.aiModelsPage.modelsSectionDescription')"
        class="max-w-none"
      >
        <EmptyState
          v-if="models.length === 0"
          :title="$t('pages.aiModelsPage.emptyModelsTitle')"
          :description="$t('pages.aiModelsPage.emptyModelsDescription')"
        >
          <template #icon>
            <Bot />
          </template>
          <Button class="mt-2" @click="openCreate">
            <Plus class="mr-2 size-4" />
            {{ $t("pages.aiModelsPage.addModel") }}
          </Button>
        </EmptyState>

        <div v-else class="rounded-lg border overflow-hidden">
          <div class="overflow-x-auto">
            <Table class="table-fixed">
              <TableHeader>
                <TableRow>
                  <TableHead class="w-28">{{ $t("common.status") }}</TableHead>
                  <TableHead class="w-[18%]">{{ $t("common.name") }}</TableHead>
                  <TableHead class="w-36">{{ $t("pages.aiModelsPage.providerLabel") }}</TableHead>
                  <TableHead class="w-[18%]">Model ID</TableHead>
                  <TableHead>API URL</TableHead>
                  <TableHead class="w-36"></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="model in models" :key="model.id">
                  <TableCell>
                    <Switch
                      :model-value="model.isEnabled"
                      class="scale-[0.8]"
                      @update:model-value="(v) => setModelEnabled(model, v)"
                    />
                  </TableCell>
                  <TableCell class="max-w-0">
                    <div class="truncate font-medium" :title="model.name">{{ model.name }}</div>
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary">
                      {{ isAnthropic(model.provider) ? "Anthropic" : "OpenAI-compatible" }}
                    </Badge>
                  </TableCell>
                  <TableCell class="max-w-0">
                    <code class="block truncate rounded bg-muted px-1.5 py-0.5 text-xs" :title="model.modelId">
                      {{ model.modelId }}
                    </code>
                  </TableCell>
                  <TableCell class="max-w-0 text-muted-foreground text-sm">
                    <div class="flex min-w-0 items-center gap-1.5 overflow-hidden">
                      <span class="truncate" :title="model.apiUrl">{{ model.apiUrl }}</span>
                      <Network
                        v-if="model.proxyUrl"
                        class="size-3 shrink-0 text-primary/70"
                        :title="$t('pages.aiModelsPage.proxyTitle', { url: model.proxyUrl })"
                      />
                    </div>
                  </TableCell>
                  <TableCell>
                    <div class="flex items-center justify-end gap-1">
                      <Button
                        variant="ghost"
                        size="sm"
                        :disabled="testingId === model.id"
                        @click="testModel(model)"
                      >
                        <Loader2 v-if="testingId === model.id" class="mr-1 size-3 animate-spin" />
                        <Zap v-else class="mr-1 size-3" />
                        {{ $t("pages.aiModelsPage.testButton") }}
                      </Button>
                      <Button variant="ghost" size="icon" @click="openEdit(model)">
                        <Pencil class="size-4" />
                        <span class="sr-only">{{ $t("common.edit") }}</span>
                      </Button>
                      <Button variant="ghost" size="icon" class="text-destructive hover:text-destructive" @click="deleteModel(model)">
                        <Trash2 class="size-4" />
                        <span class="sr-only">{{ $t("common.delete") }}</span>
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </div>
      </SettingsFormSection>

      <SettingsFormSection
        :title="$t('pages.aiModelsPage.personalSectionTitle')"
        :description="$t('pages.aiModelsPage.personalSectionDescription')"
      >
        <SettingsFormField :label="$t('pages.aiModelsPage.defaultModelLabel')">
          <Select :model-value="defaultModelId" @update:model-value="onDefaultModelChange">
            <SelectTrigger class="w-full max-w-md">
              <SelectValue :placeholder="$t('pages.aiModelsPage.chooseModelPlaceholder')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="m in models" :key="m.id" :value="String(m.id)" :text-value="m.name">
                {{ m.name }}
                <span class="ml-2 text-xs text-muted-foreground/60 font-mono">{{ m.modelId }}</span>
              </SelectItem>
            </SelectContent>
          </Select>
          <template #description>
            {{ $t("pages.aiModelsPage.defaultModelHint") }}
          </template>
        </SettingsFormField>

        <SettingsFormField :label="$t('pages.aiModelsPage.storeDescriptionLabel')">
          <Textarea
            v-model="settingsForm.storeDescription"
            :placeholder="$t('pages.aiModelsPage.storeDescriptionPlaceholder')"
            class="min-h-[80px] resize-none"
          />
          <template #description>
            {{ $t("pages.aiModelsPage.storeDescriptionHint") }}
          </template>
        </SettingsFormField>

        <div class="grid gap-5 sm:grid-cols-3">
          <SettingsFormField :label="$t('pages.aiModelsPage.communicationStyleLabel')">
            <Select v-model="settingsForm.communicationStyle">
              <SelectTrigger class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="professional">{{ $t("pages.aiModelsPage.communicationStyleProfessional") }}</SelectItem>
                <SelectItem value="friendly">{{ $t("pages.aiModelsPage.communicationStyleFriendly") }}</SelectItem>
                <SelectItem value="expert">{{ $t("pages.aiModelsPage.communicationStyleExpert") }}</SelectItem>
              </SelectContent>
            </Select>
          </SettingsFormField>

          <SettingsFormField :label="$t('pages.aiModelsPage.responseBrevityLabel')">
            <Select v-model="settingsForm.responseBrevity">
              <SelectTrigger class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="brief">{{ $t("pages.aiModelsPage.responseBrevityBrief") }}</SelectItem>
                <SelectItem value="balanced">{{ $t("pages.aiModelsPage.responseBrevityBalanced") }}</SelectItem>
                <SelectItem value="detailed">{{ $t("pages.aiModelsPage.responseBrevityDetailed") }}</SelectItem>
              </SelectContent>
            </Select>
          </SettingsFormField>

          <SettingsFormField :label="$t('pages.aiModelsPage.emojiUsageLabel')">
            <Select v-model="settingsForm.emojiUsage">
              <SelectTrigger class="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="none">{{ $t("pages.aiModelsPage.emojiUsageNone") }}</SelectItem>
                <SelectItem value="minimal">{{ $t("pages.aiModelsPage.emojiUsageMinimal") }}</SelectItem>
                <SelectItem value="standard">{{ $t("pages.aiModelsPage.emojiUsageStandard") }}</SelectItem>
              </SelectContent>
            </Select>
          </SettingsFormField>
        </div>

        <template #footer>
          <Button :disabled="isSettingsSaving" @click="saveSettings">
            <Loader2 v-if="isSettingsSaving" class="mr-2 size-4 animate-spin" />
            <Save v-else class="mr-2 size-4" />
            {{ isSettingsSaving ? $t("pages.aiModelsPage.saving") : $t("common.save") }}
          </Button>
        </template>
      </SettingsFormSection>
    </PageSection>

    <AlertDialog
      :open="confirmDialog.open"
      @update:open="(v) => { if (!v) confirmDialog.resolve?.(false); confirmDialog.open = v; }"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmDialog.title }}</AlertDialogTitle>
          <AlertDialogDescription>{{ confirmDialog.message }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel @click="confirmDialog.resolve(false); confirmDialog.open = false">{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            class="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            @click="confirmDialog.resolve(true); confirmDialog.open = false"
          >{{ $t("common.delete") }}</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <Dialog v-model:open="isDialogOpen">
      <DialogContent size="xl">
        <DialogHeader>
          <DialogTitle>{{ editingModel ? $t("pages.aiModelsPage.editModelTitle") : $t("pages.aiModelsPage.addModelDialogTitle") }}</DialogTitle>
          <DialogDescription>
            {{ editingModel ? $t("pages.aiModelsPage.editModelDescription") : $t("pages.aiModelsPage.addModelDescription") }}
          </DialogDescription>
        </DialogHeader>

        <div class="grid gap-4 py-2">
          <div class="grid gap-2">
            <Label>{{ $t("common.name") }}</Label>
            <Input v-model="form.name" placeholder="Claude Sonnet" />
          </div>

          <div class="grid gap-2">
            <Label>{{ $t("pages.aiModelsPage.providerLabel") }}</Label>
            <Select v-model="form.provider">
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="$t('pages.aiModelsPage.chooseProviderPlaceholder')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="0">Anthropic</SelectItem>
                <SelectItem value="1">OpenAI-compatible</SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div class="grid gap-2">
            <Label>API URL</Label>
            <Input
              v-model="form.apiUrl"
              :placeholder="form.provider === '0'
                ? 'https://api.anthropic.com/v1/messages'
                : $t('pages.aiModelsPage.apiUrlPlaceholderOpenAi')"
            />
            <p v-if="form.provider === '1'" class="text-xs text-muted-foreground">
              {{ $t("pages.aiModelsPage.baseUrlOnlyPrefix") }} <code>/v1/chat/completions</code> {{ $t("pages.aiModelsPage.baseUrlOnlySuffix") }}
            </p>
          </div>

          <div class="grid gap-2">
            <Label>{{ $t("pages.aiModelsPage.proxyLabel") }} <span class="text-muted-foreground font-normal text-xs">{{ $t("pages.aiModelsPage.optionalSuffix") }}</span></Label>
            <PasswordInput
              v-model="form.proxyUrl"
              :placeholder="$t('pages.aiModelsPage.proxyPlaceholder')"
              autocomplete="off"
            />
            <p class="text-xs text-muted-foreground">
              {{ $t("pages.aiModelsPage.proxyHint") }}
            </p>
          </div>

          <div v-if="!editingModel" class="grid gap-2">
            <Label>{{ $t("pages.aiModelsPage.apiKeyLabel") }}</Label>
            <PasswordInput v-model="form.apiKey" placeholder="sk-ant-..." autocomplete="off" />
          </div>

          <div class="grid gap-2">
            <div class="flex items-center justify-between">
              <Label>Model ID</Label>
              <Button variant="ghost" size="sm" class="h-6 px-2 text-xs" :disabled="isDiscovering" @click="discoverModels">
                <Loader2 v-if="isDiscovering" class="mr-1 size-3 animate-spin" />
                <RefreshCw v-else class="mr-1 size-3" />
                {{ $t("pages.aiModelsPage.fetchListButton") }}
              </Button>
            </div>
            <Select v-if="modelOptions.length" v-model="form.modelId">
              <SelectTrigger class="w-full">
                <SelectValue :placeholder="$t('pages.aiModelsPage.chooseModelPlaceholder')" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem v-for="id in modelOptions" :key="id" :value="id">{{ id }}</SelectItem>
              </SelectContent>
            </Select>
            <Input v-else v-model="form.modelId" placeholder="claude-sonnet-4-6" />
            <Button
              v-if="modelOptions.length"
              type="button"
              variant="link"
              class="h-auto p-0 text-xs text-muted-foreground w-fit"
              @click="modelOptions = []"
            >
              {{ $t("pages.aiModelsPage.enterManuallyButton") }}
            </Button>
          </div>

        </div>

        <DialogFooter>
          <Button variant="ghost" @click="closeDialog">{{ $t("common.cancel") }}</Button>
          <Button :disabled="isSaving" @click="save">
            <Loader2 v-if="isSaving" class="mr-2 size-4 animate-spin" />
            {{ isSaving ? $t("pages.aiModelsPage.saving") : $t("common.save") }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
