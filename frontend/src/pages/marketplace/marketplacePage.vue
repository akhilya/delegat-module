<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import {
  Plus,
  Store,
  Loader2,
  ChevronRight,
  Package,
  Trash2,
  FolderTree,
  CheckCircle,
  XCircle,
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
  Card,
} from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import Switch from "@/components/ui/switch/Switch.vue";
import { PageHeader } from "@/components/ui/page-header";
import { EmptyState } from "@/components/ui/empty-state";
import { PAGE_ADMIN_PLUGINS } from "@/router";

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Label,
    Badge,
    Plus,
    Store,
    Loader2,
    ChevronRight,
    Package,
    Trash2,
    FolderTree,
    CheckCircle,
    XCircle,
    Settings,
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
    Card,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Switch,
    PageHeader,
    EmptyState,
  },
  data() {
    return {
      connections: [],
      adapters: [],
      isLoading: false,
      isCreating: false,
      isDeleting: false,
      showCreateDialog: false,
      newConnection: {
        name: "",
        adapterKey: "",
        credentialsJson: "",
        isActive: true,
      },
      selectedAdapter: null,
      credentialValues: {},
      breadcrumbs: [{ label: "Адаптеры" }],
    };
  },
  computed: {
    selectedAdapterSchema() {
      if (!this.selectedAdapter) return null;
      const adapter = this.adapters.find((a) => a.key === this.selectedAdapter);
      return adapter?.credentialsSchema;
    },
    credentialFields() {
      // Handle both PascalCase (C# local) and camelCase (remote adapter) naming
      const schema = this.selectedAdapterSchema;
      if (!schema) return [];
      return schema.fields || schema.Fields || [];
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
          this.connections = extractResponseData(response, []);
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

    onAdapterSelect(adapterKey) {
      this.selectedAdapter = adapterKey;
      this.newConnection.adapterKey = adapterKey;
      this.credentialValues = {};
    },

    buildCredentialsJson() {
      return JSON.stringify(this.credentialValues);
    },

    async handleCreateConnection() {
      if (!this.newConnection.name.trim()) {
        displaySonnerError("Введите название подключения");
        return;
      }
      if (!this.newConnection.adapterKey) {
        displaySonnerError("Выберите адаптер");
        return;
      }

      this.isCreating = true;
      await this.createConnection({
        data: {
          name: this.newConnection.name.trim(),
          adapterKey: this.newConnection.adapterKey,
          credentialsJson: this.buildCredentialsJson(),
          isActive: this.newConnection.isActive,
        },
        onSuccess: (response) => {
          displaySonnerSuccess("Подключение создано");
          this.resetForm();
          this.showCreateDialog = false;
          const data = extractResponseData(response, {});
          this.$router.push(`/adapter/${data.id}`);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    async handleDeleteConnection(connectionId) {
      this.isDeleting = true;
      await this.deleteConnection({
        connectionId,
        onSuccess: () => {
          displaySonnerSuccess("Подключение удалено");
          this.fetchConnections();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isDeleting = false;
    },

    resetForm() {
      this.newConnection = {
        name: "",
        adapterKey: "",
        credentialsJson: "",
        isActive: true,
      };
      this.selectedAdapter = null;
      this.credentialValues = {};
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
  },
  mounted() {
    this.fetchData();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      title="Адаптеры"
      description="Управление подключениями к адаптерам для синхронизации товаров"
    >
      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            Добавить подключение
          </Button>
        </DialogTrigger>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Новое подключение к адаптеру</DialogTitle>
            <DialogDescription>
              Выберите адаптер и введите данные для подключения
            </DialogDescription>
          </DialogHeader>
          <div class="py-4 space-y-4">
            <!-- No plugins available -->
            <EmptyState
              v-if="adapters.length === 0"
              title="Нет доступных плагинов"
              description='Для подключения к адаптерам необходимо установить соответствующие плагины. Перейдите в раздел "Плагины" для установки.'
            >
              <template #icon>
                <Package />
              </template>
              <Button variant="outline" class="mt-4" @click="openPlugins">
                Перейти к плагинам
              </Button>
            </EmptyState>

            <template v-else>
              <div class="space-y-2">
                <Label>Название подключения</Label>
                <Input
                  v-model="newConnection.name"
                  placeholder="Например: Основной магазин WB"
                />
              </div>

              <div class="space-y-2">
                <Label>Адаптер</Label>
                <Select v-model="selectedAdapter" @update:modelValue="onAdapterSelect">
                  <SelectTrigger>
                    <SelectValue placeholder="Выберите адаптер" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem
                      v-for="adapter in adapters"
                      :key="adapter.key"
                      :value="adapter.key"
                    >
                      {{ adapter.displayName }}
                    </SelectItem>
                  </SelectContent>
                </Select>
                <p v-if="selectedAdapter" class="text-sm text-muted-foreground">
                  {{ adapters.find((a) => a.key === selectedAdapter)?.description }}
                </p>
              </div>

              <template v-if="credentialFields?.length">
                <div
                  v-for="field in credentialFields"
                  :key="field.key"
                  class="space-y-2"
                >
                  <Label>
                    {{ field.displayName }}
                    <span v-if="field.isRequired" class="text-destructive">*</span>
                  </Label>
                  <Input
                    v-model="credentialValues[field.key]"
                    :type="field.isSecret ? 'password' : 'text'"
                    :placeholder="field.placeholder || field.description"
                    :required="field.isRequired"
                  />
                  <p v-if="field.description" class="text-xs text-muted-foreground">
                    {{ field.description }}
                  </p>
                </div>
              </template>

              <div class="flex items-center gap-2">
                <Switch :model-value="newConnection.isActive" @update:model-value="newConnection.isActive = $event" id="active" />
                <Label for="active">Активное подключение</Label>
              </div>
            </template>
          </div>
          <DialogFooter v-if="adapters.length > 0">
            <DialogClose as-child>
              <Button variant="outline" @click="resetForm">Отмена</Button>
            </DialogClose>
            <Button @click="handleCreateConnection" :disabled="isCreating">
              <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
              Создать
            </Button>
          </DialogFooter>
          <DialogFooter v-else>
            <DialogClose as-child>
              <Button variant="outline">Закрыть</Button>
            </DialogClose>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </PageHeader>

    <PageSection
      :loading="isLoading"
      :empty="connections.length === 0"
      :empty-icon="Store"
      empty-title="Нет подключений к адаптерам"
      empty-description="Добавьте первое подключение для синхронизации товаров"
    >

      <Card class="p-0 gap-0 overflow-hidden">
        <ul class="divide-y divide-border/60">
          <li
            v-for="conn in connections"
            :key="conn.id"
            class="flex items-center gap-4 px-5 py-3.5 hover:bg-muted/40 cursor-pointer group transition-colors"
            @click="openConnection(conn.id)"
          >
            <!-- Status dot left of icon — green=active, neutral=inactive. Subtler than a Badge. -->
            <span
              class="size-2 rounded-full shrink-0"
              :class="conn.isActive ? 'bg-success' : 'bg-muted-foreground/40'"
              :title="conn.isActive ? 'Активен' : 'Неактивен'"
            />
            <Store class="size-5 text-muted-foreground shrink-0" />
            <div class="min-w-0 flex-1">
              <div class="font-medium truncate">{{ conn.name }}</div>
              <div class="text-sm text-muted-foreground truncate">{{ getAdapterName(conn.adapterKey) }}</div>
            </div>
            <div class="flex items-center gap-4 text-sm text-muted-foreground shrink-0 tabular-nums">
              <span class="flex items-center gap-1.5" title="Маппингов категорий">
                <FolderTree class="size-4" />
                {{ conn.categoryMappingsCount || 0 }}
              </span>
              <span class="flex items-center gap-1.5" title="Связанных товаров">
                <Package class="size-4" />
                {{ conn.productLinksCount || 0 }}
              </span>
            </div>
            <Button
              variant="ghost"
              size="icon"
              class="opacity-0 group-hover:opacity-100 transition-opacity shrink-0"
              @click.stop="openConnection(conn.id)"
              title="Настройка"
            >
              <Settings class="size-4" />
            </Button>
            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button
                  variant="ghost"
                  size="icon"
                  class="opacity-0 group-hover:opacity-100 transition-opacity shrink-0"
                  @click.stop
                >
                  <Trash2 class="size-4 text-destructive" />
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Удалить подключение?</AlertDialogTitle>
                  <AlertDialogDescription>
                    Вы уверены что хотите удалить подключение "{{ conn.name }}"? Это
                    удалит все маппинги категорий и связи товаров.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Отмена</AlertDialogCancel>
                  <AlertDialogAction
                    @click="handleDeleteConnection(conn.id)"
                    :disabled="isDeleting"
                  >
                    <Loader2 v-if="isDeleting" class="size-4 mr-2 animate-spin" />
                    Удалить
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
            <ChevronRight class="size-4 text-muted-foreground/60 shrink-0" />
          </li>
        </ul>
      </Card>
    </PageSection>
  </Page>
</template>
