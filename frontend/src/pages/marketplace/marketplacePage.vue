<script>
import Page from "@/components/page.vue";
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
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
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

export default {
  components: {
    Page,
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
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
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
      breadcrumbs: [{ label: "Маркетплейсы" }],
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
        displaySonnerError("Выберите маркетплейс");
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
          this.$router.push(`/marketplace/${data.id}`);
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
      this.$router.push(`/marketplace/${connectionId}`);
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
      title="Маркетплейсы"
      description="Управление подключениями к маркетплейсам для синхронизации товаров"
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
            <DialogTitle>Новое подключение к маркетплейсу</DialogTitle>
            <DialogDescription>
              Выберите маркетплейс и введите данные для подключения
            </DialogDescription>
          </DialogHeader>
          <div class="py-4 space-y-4">
            <!-- No plugins available -->
            <EmptyState
              v-if="adapters.length === 0"
              title="Нет доступных плагинов"
              description='Для подключения к маркетплейсам необходимо установить соответствующие плагины. Перейдите в раздел "Плагины" для установки.'
            >
              <template #icon>
                <Package class="size-12 mb-4 text-muted-foreground" />
              </template>
              <Button variant="outline" class="mt-4" @click="$router.push('/marketplace/plugins')">
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
                <Label>Маркетплейс</Label>
                <Select v-model="selectedAdapter" @update:modelValue="onAdapterSelect">
                  <SelectTrigger>
                    <SelectValue placeholder="Выберите маркетплейс" />
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

    <div class="border rounded-lg h-full w-full grow mb-4 p-4">
      <EmptyState
        v-if="isLoading || connections.length === 0"
        :loading="isLoading"
        title="Нет подключений к маркетплейсам"
        description="Добавьте первое подключение для синхронизации товаров"
      >
        <template #icon>
          <Store class="size-12 mb-4 text-muted-foreground" />
        </template>
      </EmptyState>

      <div v-else class="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <Card
          v-for="conn in connections"
          :key="conn.id"
          class="cursor-pointer hover:bg-accent/50 transition-colors group"
        >
          <CardHeader @click="openConnection(conn.id)">
            <CardTitle class="flex items-center justify-between">
              <span class="flex items-center gap-2">
                <Store class="size-5" />
                {{ conn.name }}
              </span>
              <Badge :variant="conn.isActive ? 'default' : 'secondary'">
                {{ conn.isActive ? "Активен" : "Неактивен" }}
              </Badge>
            </CardTitle>
            <CardDescription>
              {{ getAdapterName(conn.adapterKey) }}
            </CardDescription>
          </CardHeader>
          <CardContent class="flex justify-between items-center">
            <div class="flex items-center gap-4 text-sm text-muted-foreground">
              <span class="flex items-center gap-1">
                <FolderTree class="size-4" />
                {{ conn.categoryMappingsCount || 0 }} категорий
              </span>
              <span class="flex items-center gap-1">
                <Package class="size-4" />
                {{ conn.productLinksCount || 0 }} товаров
              </span>
            </div>
            <div class="flex items-center gap-1">
              <Button
                variant="ghost"
                size="icon"
                class="opacity-0 group-hover:opacity-100 transition-opacity"
                @click.stop="openConnection(conn.id)"
              >
                <Settings class="size-4" />
              </Button>
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="opacity-0 group-hover:opacity-100 transition-opacity"
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
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  </Page>
</template>
