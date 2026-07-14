<script>

import PageSection from "@/components/pageSection.vue";


import { PageHeader } from "@/components/ui/page-header";
import { Button } from "@/components/ui/button";
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
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export default {
  components: {
    PageSection,
    PageHeader,
    Button,
    Input,
    Label,
    Badge,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
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

    formatLoadedAt(value) {
      if (!value) return "—";
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) return "—";
      return date.toLocaleString("ru-RU", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      });
    },
  },
  mounted() {
    this.fetchData();
  },
};
</script>

<template>
  <div>
    <PageHeader
      title="Плагины"
      description="Управление DLL-плагинами для интеграции с адаптерами"
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

    <PageSection
      :loading="isLoading"
      :empty="plugins.length === 0"
      :empty-icon="Puzzle"
      empty-title="Нет установленных плагинов"
      empty-description="Загрузите DLL-плагин для интеграции с адаптером"
    >
      <template #empty-action>
        <Button @click="showUploadDialog = true">
          <Upload class="size-4 mr-2" />
          Загрузить плагин
        </Button>
      </template>

      <div class="rounded-lg border overflow-hidden">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow class="hover:bg-transparent">
                <TableHead class="px-3">Плагин</TableHead>
                <TableHead class="hidden md:table-cell px-3">Ключ</TableHead>
                <TableHead class="w-24 px-3">Версия</TableHead>
                <TableHead class="hidden lg:table-cell px-3">Файл</TableHead>
                <TableHead class="hidden xl:table-cell px-3">Загружен</TableHead>
                <TableHead class="w-28 px-3">Тип</TableHead>
                <TableHead class="w-24 px-2" />
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow
                v-for="plugin in plugins"
                :key="plugin.key"
                class="group"
              >
                <TableCell class="px-3 align-top">
                  <div class="flex items-start gap-2.5 min-w-0">
                    <Puzzle class="size-4 text-muted-foreground shrink-0 mt-0.5" />
                    <div class="min-w-0">
                      <div class="font-medium break-words">{{ plugin.displayName }}</div>
                      <div
                        v-if="plugin.description"
                        class="text-xs text-muted-foreground mt-0.5 line-clamp-2"
                      >
                        {{ plugin.description }}
                      </div>
                      <code class="text-xs text-muted-foreground font-mono mt-1 block md:hidden">
                        {{ plugin.key }}
                      </code>
                    </div>
                  </div>
                </TableCell>
                <TableCell class="hidden md:table-cell px-3 align-top font-mono text-xs text-muted-foreground">
                  {{ plugin.key }}
                </TableCell>
                <TableCell class="px-3 align-top">
                  <Badge variant="outline" class="text-xs tabular-nums">
                    v{{ plugin.version }}
                  </Badge>
                </TableCell>
                <TableCell class="hidden lg:table-cell px-3 align-top font-mono text-xs text-muted-foreground max-w-[12rem] truncate">
                  {{ plugin.filePath || "—" }}
                </TableCell>
                <TableCell class="hidden xl:table-cell px-3 align-top text-xs text-muted-foreground tabular-nums whitespace-nowrap">
                  {{ formatLoadedAt(plugin.loadedAt) }}
                </TableCell>
                <TableCell class="px-3 align-top">
                  <Badge :variant="plugin.isSystem ? 'secondary' : 'outline'">
                    {{ plugin.isSystem ? "Системный" : "Пользовательский" }}
                  </Badge>
                </TableCell>
                <TableCell class="px-2 align-top">
                  <div class="flex items-center justify-end gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                    <Button
                      variant="ghost"
                      size="icon"
                      class="size-8"
                      @click="handleReload(plugin.key)"
                      :disabled="isReloading === plugin.key"
                      title="Перезагрузить"
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
                          class="size-8"
                          title="Удалить"
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
                            }}"? Подключения, использующие этот плагин, перестанут
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
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>
      </div>
    </PageSection>
  </div>
</template>
