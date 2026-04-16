<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { PageHeader } from "@/components/ui/page-header";
import { EmptyState } from "@/components/ui/empty-state";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import {
  Plus,
  Puzzle,
  Loader2,
  Trash2,
  RefreshCw,
  Upload,
  Package,
  Info,
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

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    PageHeader,
    EmptyState,
    Button,
    Input,
    Label,
    Badge,
    Plus,
    Puzzle,
    Loader2,
    Trash2,
    RefreshCw,
    Upload,
    Package,
    Info,
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
  },
  computed: {
    pluginsWithCapabilities() {
      return this.plugins.map((plugin) => ({
        ...plugin,
        formattedCapabilities: this.formatCapabilities(plugin.capabilities),
      }));
    },
  },
  data() {
    return {
      plugins: [],
      isLoading: false,
      isUploading: false,
      isDeleting: false,
      isReloading: null,
      showUploadDialog: false,
      selectedFile: null,
      breadcrumbs: [
        { label: "Маркетплейсы", href: "/marketplace" },
        { label: "Плагины" },
      ],
    };
  },
  methods: {
    ...mapActions([
      "getPlugins",
      "uploadPlugin",
      "deletePlugin",
      "reloadPlugin",
    ]),

    async fetchData() {
      this.isLoading = true;
      await this.getPlugins({
        onSuccess: (response) => {
          this.plugins = extractResponseData(response, []);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    handleFileSelect(event) {
      const file = event.target.files[0];
      if (file) {
        if (!file.name.endsWith('.dll')) {
          displaySonnerError("Выберите файл с расширением .dll");
          return;
        }
        this.selectedFile = file;
      }
    },

    async handleUpload() {
      if (!this.selectedFile) {
        displaySonnerError("Выберите файл плагина");
        return;
      }

      this.isUploading = true;

      const formData = new FormData();
      formData.append("file", this.selectedFile);

      await this.uploadPlugin({
        formData,
        onSuccess: () => {
          displaySonnerSuccess("Плагин загружен");
          this.resetUploadForm();
          this.showUploadDialog = false;
          this.fetchData();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });

      this.isUploading = false;
    },

    async handleDelete(pluginKey) {
      this.isDeleting = true;

      await this.deletePlugin({
        pluginKey,
        onSuccess: () => {
          displaySonnerSuccess("Плагин удалён");
          this.fetchData();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });

      this.isDeleting = false;
    },

    async handleReload(pluginKey) {
      this.isReloading = pluginKey;

      await this.reloadPlugin({
        pluginKey,
        onSuccess: () => {
          displaySonnerSuccess("Плагин перезагружен");
          this.fetchData();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });

      this.isReloading = null;
    },

    resetUploadForm() {
      this.selectedFile = null;
      // Reset file input
      const fileInput = this.$refs.fileInput;
      if (fileInput) {
        fileInput.value = "";
      }
    },

    formatCapabilities(capabilities) {
      if (!capabilities) return [];

      const capMap = {
        getCategories: "Категории",
        getCategoryFields: "Поля категорий",
        exportProduct: "Экспорт товаров",
        importProduct: "Импорт товаров",
        importAllProducts: "Массовый импорт",
        updateSku: "Обновление SKU",
        importPrices: "Импорт цен",
        exportPrices: "Экспорт цен",
        exportStocks: "Экспорт остатков",
      };

      return Object.entries(capabilities)
        .filter(([_, value]) => value === true)
        .map(([key]) => capMap[key] || key);
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
      title="Плагины"
      description="Управление DLL-плагинами для интеграции с маркетплейсами"
    >
      <Dialog v-model:open="showUploadDialog">
        <DialogTrigger as-child>
          <Button>
            <Upload class="size-4 mr-2" />
            Загрузить плагин
          </Button>
        </DialogTrigger>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Загрузить плагин</DialogTitle>
            <DialogDescription>
              Выберите DLL файл плагина для загрузки
            </DialogDescription>
          </DialogHeader>

          <div class="py-4 space-y-4">
            <div class="space-y-2">
              <Label>Файл плагина (.dll)</Label>
              <div class="border-2 border-dashed rounded-lg p-6 text-center">
                <input
                  ref="fileInput"
                  type="file"
                  accept=".dll"
                  class="hidden"
                  @change="handleFileSelect"
                />
                <div v-if="!selectedFile" class="space-y-2">
                  <Package class="size-12 mx-auto text-muted-foreground" />
                  <p class="text-sm text-muted-foreground">
                    Перетащите файл сюда или
                  </p>
                  <Button
                    variant="outline"
                    size="sm"
                    @click="$refs.fileInput.click()"
                  >
                    Выберите файл
                  </Button>
                </div>
                <div v-else class="space-y-2">
                  <Puzzle class="size-12 mx-auto text-primary" />
                  <p class="font-medium">{{ selectedFile.name }}</p>
                  <p class="text-xs text-muted-foreground">
                    {{ (selectedFile.size / 1024).toFixed(1) }} KB
                  </p>
                  <Button
                    variant="ghost"
                    size="sm"
                    @click="resetUploadForm"
                  >
                    Выбрать другой
                  </Button>
                </div>
              </div>
            </div>

            <div class="bg-muted/50 p-3 rounded-lg text-sm text-muted-foreground">
              <Info class="size-4 inline mr-2" />
              Плагин должен реализовывать интерфейс IMarketplacePlugin из SDK
            </div>
          </div>

          <DialogFooter>
            <DialogClose as-child>
              <Button variant="outline" @click="resetUploadForm">Отмена</Button>
            </DialogClose>
            <Button
              @click="handleUpload"
              :disabled="isUploading || !selectedFile"
            >
              <Loader2 v-if="isUploading" class="size-4 mr-2 animate-spin" />
              Загрузить
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </PageHeader>

    <div class="border rounded-lg h-full w-full grow mb-4 p-4">
      <EmptyState v-if="isLoading" loading />
      <EmptyState
        v-else-if="plugins.length === 0"
        title="Нет установленных плагинов"
        description="Загрузите DLL-плагин для интеграции с маркетплейсом"
      >
        <template #icon>
          <Puzzle class="size-12 mb-4 text-muted-foreground" />
        </template>
        <Button @click="showUploadDialog = true">
          <Upload class="size-4 mr-2" />
          Загрузить плагин
        </Button>
      </EmptyState>

      <Card v-else class="p-0 gap-0 overflow-hidden">
        <ul class="divide-y divide-border/60">
          <li
            v-for="plugin in pluginsWithCapabilities"
            :key="plugin.key"
            class="flex items-start gap-4 px-5 py-3.5 hover:bg-muted/40 group transition-colors"
          >
            <Puzzle class="size-5 text-muted-foreground shrink-0 mt-0.5" />
            <div class="min-w-0 flex-1">
              <div class="flex items-center gap-2 flex-wrap">
                <span class="font-medium truncate">{{ plugin.displayName }}</span>
                <Badge variant="outline" class="text-xs tabular-nums">v{{ plugin.version }}</Badge>
                <code class="text-xs text-muted-foreground font-mono">{{ plugin.key }}</code>
              </div>
              <div v-if="plugin.description" class="text-sm text-muted-foreground mt-0.5">
                {{ plugin.description }}
              </div>
              <div v-if="plugin.formattedCapabilities.length > 0" class="flex flex-wrap gap-1 mt-2">
                <Badge
                  v-for="cap in plugin.formattedCapabilities"
                  :key="cap"
                  variant="secondary"
                  class="text-[11px] font-normal"
                >
                  {{ cap }}
                </Badge>
              </div>
            </div>
            <div class="flex items-center gap-1 shrink-0">
              <Button
                variant="ghost"
                size="icon"
                @click="handleReload(plugin.key)"
                :disabled="isReloading === plugin.key"
                title="Перезагрузить"
                class="opacity-0 group-hover:opacity-100 transition-opacity"
              >
                <Loader2
                  v-if="isReloading === plugin.key"
                  class="size-4 animate-spin"
                />
                <RefreshCw v-else class="size-4" />
              </Button>
              <AlertDialog v-if="!plugin.isSystem">
                <AlertDialogTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    title="Удалить"
                    class="opacity-0 group-hover:opacity-100 transition-opacity"
                  >
                    <Trash2 class="size-4 text-destructive" />
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>Удалить плагин?</AlertDialogTitle>
                    <AlertDialogDescription>
                      Вы уверены что хотите удалить плагин "{{
                        plugin.displayName
                      }}"? Подключения использующие этот плагин перестанут
                      работать.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Отмена</AlertDialogCancel>
                    <AlertDialogAction
                      @click="handleDelete(plugin.key)"
                      :disabled="isDeleting"
                    >
                      <Loader2 v-if="isDeleting" class="size-4 mr-2 animate-spin" />
                      Удалить
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            </div>
          </li>
        </ul>
      </Card>
    </div>
  </Page>
</template>
