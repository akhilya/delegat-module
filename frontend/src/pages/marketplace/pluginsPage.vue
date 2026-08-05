<script>

import PageSection from "@/components/pageSection.vue";


import { Button } from "@/components/ui/button";
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
  FileCode2,
  X,
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
import { useStoreDateTime } from "@/composables/useStoreDateTime";

export default {
  setup() {
    const { formatDateTime: formatStoreDateTime, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { formatStoreDateTime };
  },
  components: {
    PageSection,
    Button,
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
    FileCode2,
    X,
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
      registryPlugins: [],
      isLoading: false,
      isUploading: false,
      isDeleting: false,
      isReloading: null,
      showUploadDialog: false,
      selectedFile: null,
      isDraggingUpload: false,
    };
  },
  computed: {
    availableFromRegistry() {
      return (this.registryPlugins || []).filter((p) => !p.installed);
    },
    selectedFileSizeLabel() {
      if (!this.selectedFile) return "";
      const bytes = this.selectedFile.size;
      if (bytes < 1024) return `${bytes} B`;
      if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
      return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    },
  },
  watch: {
    showUploadDialog(open) {
      if (!open) this.resetUploadForm();
    },
  },
  methods: {
    ...mapActions([
      "getPlugins",
      "uploadPlugin",
      "deletePlugin",
      "reloadPlugin",
      "getReferencePlugins",
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
      await this.getReferencePlugins({
        onSuccess: (response) => {
          const items = response.data?.value ?? response.data ?? [];
          this.registryPlugins = Array.isArray(items) ? items : [];
        },
        onError: () => {
          this.registryPlugins = [];
        },
      });
      this.isLoading = false;
    },

    acceptDllFile(file) {
      if (!file) return false;
      const name = file.name || "";
      if (!name.toLowerCase().endsWith(".dll")) {
        displaySonnerError(this.$t("marketplace.pluginsPage.selectDllFile"));
        return false;
      }
      this.selectedFile = file;
      this.isDraggingUpload = false;
      return true;
    },

    handleFileSelect(event) {
      const file = event.target.files?.[0];
      if (!this.acceptDllFile(file) && this.$refs.fileInput) {
        this.$refs.fileInput.value = "";
      }
    },

    handleUploadDrop(event) {
      this.isDraggingUpload = false;
      const file = event.dataTransfer?.files?.[0];
      this.acceptDllFile(file);
    },

    openFilePicker() {
      this.$refs.fileInput?.click();
    },

    async handleUpload() {
      if (!this.selectedFile) {
        displaySonnerError(this.$t("marketplace.pluginsPage.selectPluginFile"));
        return;
      }

      this.isUploading = true;

      const formData = new FormData();
      formData.append("file", this.selectedFile);

      await this.uploadPlugin({
        formData,
        onSuccess: () => {
          displaySonnerSuccess(this.$t("marketplace.pluginsPage.pluginUploaded"));
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
          displaySonnerSuccess(this.$t("marketplace.pluginsPage.pluginDeleted"));
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
          displaySonnerSuccess(this.$t("marketplace.pluginsPage.pluginReloaded"));
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
      this.isDraggingUpload = false;
      const fileInput = this.$refs.fileInput;
      if (fileInput) {
        fileInput.value = "";
      }
    },

    stabilityLabel(stability) {
      const labels = {
        Stable: "Stable",
        Beta: "Beta",
        Experimental: "Experimental",
      };
      return labels[stability] || stability || "Experimental";
    },

    stabilityVariant(stability) {
      if (stability === "Stable") return "default";
      if (stability === "Beta") return "secondary";
      return "outline";
    },

    formatLoadedAt(value) {
      return this.formatStoreDateTime(value);
    },
  },
  mounted() {
    this.fetchData();
  },
};
</script>

<template>
  <div>
    <div class="mb-4 flex flex-wrap items-center justify-end gap-2">
      <Dialog v-model:open="showUploadDialog">
        <DialogTrigger as-child>
          <Button>
            <Upload class="size-4 mr-2" />
            {{ $t("marketplace.pluginsPage.uploadPlugin") }}
          </Button>
        </DialogTrigger>
        <DialogContent size="lg" class="gap-0 p-0">
          <DialogHeader class="space-y-1 border-b px-6 py-4 text-left">
            <DialogTitle class="flex items-center gap-2 text-base">
              <Package class="size-4 text-muted-foreground" />
              {{ $t("marketplace.pluginsPage.uploadDialogTitle") }}
            </DialogTitle>
            <DialogDescription>
              {{ $t("marketplace.pluginsPage.uploadDialogDescription") }}
            </DialogDescription>
          </DialogHeader>

          <div class="space-y-4 px-6 py-5">
            <input
              ref="fileInput"
              type="file"
              accept=".dll,application/x-msdownload,application/octet-stream"
              class="hidden"
              @change="handleFileSelect"
            />

            <!-- Empty: drop target -->
            <button
              v-if="!selectedFile"
              type="button"
              class="group flex w-full flex-col items-center justify-center rounded-lg border border-dashed px-5 py-10 text-center outline-none transition-colors focus-visible:ring-[3px] focus-visible:ring-ring/50"
              :class="
                isDraggingUpload
                  ? 'border-foreground/40 bg-muted/40'
                  : 'border-muted-foreground/30 hover:border-foreground/35 hover:bg-muted/25'
              "
              @click="openFilePicker"
              @dragover.prevent="isDraggingUpload = true"
              @dragleave.prevent="isDraggingUpload = false"
              @drop.prevent="handleUploadDrop"
            >
              <div
                class="mb-3 flex size-10 items-center justify-center rounded-md border bg-background text-muted-foreground transition-colors group-hover:text-foreground"
              >
                <Upload class="size-4" />
              </div>
              <p class="text-sm font-medium text-foreground">
                {{ $t("marketplace.pluginsPage.dropzoneTitle") }}
              </p>
              <p class="mt-1 max-w-sm text-xs leading-relaxed text-muted-foreground">
                {{ $t("marketplace.pluginsPage.dropzoneHint") }}
              </p>
              <span
                class="mt-4 inline-flex h-8 items-center rounded-md border bg-background px-3 text-xs font-medium"
              >
                {{ $t("marketplace.pluginsPage.chooseFile") }}
              </span>
            </button>

            <!-- Selected: payload card -->
            <div
              v-else
              class="overflow-hidden rounded-lg border bg-muted/20"
            >
              <div class="flex items-start gap-3 px-3.5 py-3">
                <div
                  class="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-md border bg-background"
                >
                  <FileCode2 class="size-4 text-foreground" />
                </div>
                <div class="min-w-0 flex-1">
                  <p class="truncate text-sm font-medium text-foreground">
                    {{ selectedFile.name }}
                  </p>
                  <div
                    class="mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[11px] text-muted-foreground"
                  >
                    <span class="font-mono tabular-nums uppercase tracking-wide">.dll</span>
                    <span class="text-border" aria-hidden="true">·</span>
                    <span class="tabular-nums">{{ selectedFileSizeLabel }}</span>
                  </div>
                </div>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  class="size-8 shrink-0 text-muted-foreground"
                  :title="$t('marketplace.pluginsPage.chooseAnother')"
                  :disabled="isUploading"
                  @click="resetUploadForm"
                >
                  <X class="size-4" />
                </Button>
              </div>
              <div
                class="flex items-center justify-between gap-2 border-t bg-background/60 px-3.5 py-2"
              >
                <span class="text-[11px] text-muted-foreground">
                  {{ $t("marketplace.pluginsPage.readyToInstall") }}
                </span>
                <button
                  type="button"
                  class="text-[11px] font-medium text-foreground underline-offset-2 hover:underline"
                  :disabled="isUploading"
                  @click="openFilePicker"
                >
                  {{ $t("marketplace.pluginsPage.chooseAnother") }}
                </button>
              </div>
            </div>

            <p class="text-xs leading-relaxed text-muted-foreground">
              {{ $t("marketplace.pluginsPage.sdkInterfaceHintBefore") }}
              <code
                class="rounded-sm bg-muted px-1 py-0.5 font-mono text-[11px] text-foreground"
              >IMarketplacePlugin</code>
              {{ $t("marketplace.pluginsPage.sdkInterfaceHintAfter") }}
            </p>
          </div>

          <DialogFooter class="border-t px-6 py-4 sm:justify-between">
            <DialogClose as-child>
              <Button variant="outline" :disabled="isUploading">
                {{ $t("common.cancel") }}
              </Button>
            </DialogClose>
            <Button
              :disabled="isUploading || !selectedFile"
              @click="handleUpload"
            >
              <Loader2 v-if="isUploading" class="size-4 mr-2 animate-spin" />
              <Upload v-else class="size-4 mr-2" />
              {{
                isUploading
                  ? $t("marketplace.pluginsPage.uploading")
                  : $t("marketplace.pluginsPage.installPlugin")
              }}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>

    <div
      v-if="availableFromRegistry.length"
      class="mb-6 rounded-lg border p-4 space-y-3"
    >
      <div>
        <h2 class="text-sm font-semibold">{{ $t("marketplace.pluginsPage.registryTitle") }}</h2>
        <p class="text-xs text-muted-foreground mt-1">
          {{ $t("marketplace.pluginsPage.registryDescription") }}
        </p>
      </div>
      <div class="overflow-x-auto rounded-md border">
        <Table>
          <TableHeader>
            <TableRow class="hover:bg-transparent">
              <TableHead class="px-3">{{ $t("marketplace.pluginsPage.plugin") }}</TableHead>
              <TableHead class="px-3">{{ $t("marketplace.pluginsPage.key") }}</TableHead>
              <TableHead class="px-3">{{ $t("marketplace.pluginsPage.version") }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="plugin in availableFromRegistry" :key="plugin.key">
              <TableCell class="px-3 font-medium">{{ plugin.displayName }}</TableCell>
              <TableCell class="px-3 font-mono text-xs text-muted-foreground">{{ plugin.key }}</TableCell>
              <TableCell class="px-3">
                <Badge variant="outline" class="text-xs tabular-nums">
                  v{{ plugin.latestVersion || "-" }}
                </Badge>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </div>

    <PageSection
      :loading="isLoading"
      :empty="plugins.length === 0"
      :empty-icon="Puzzle"
      :empty-title="$t('marketplace.pluginsPage.emptyTitle')"
      :empty-description="$t('marketplace.pluginsPage.emptyDescription')"
    >
      <template #empty-action>
        <Button @click="showUploadDialog = true">
          <Upload class="size-4 mr-2" />
          {{ $t("marketplace.pluginsPage.uploadPlugin") }}
        </Button>
      </template>

      <div class="rounded-lg border overflow-hidden">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow class="hover:bg-transparent">
                <TableHead class="px-3">{{ $t("marketplace.pluginsPage.plugin") }}</TableHead>
                <TableHead class="hidden md:table-cell px-3">{{ $t("marketplace.pluginsPage.key") }}</TableHead>
                <TableHead class="w-24 px-3">{{ $t("marketplace.pluginsPage.version") }}</TableHead>
                <TableHead class="w-32 px-3">{{ $t("common.status") }}</TableHead>
                <TableHead class="hidden lg:table-cell px-3">{{ $t("marketplace.pluginsPage.file") }}</TableHead>
                <TableHead class="hidden xl:table-cell px-3">{{ $t("marketplace.pluginsPage.loaded") }}</TableHead>
                <TableHead class="w-28 px-3">{{ $t("marketplace.pluginsPage.type") }}</TableHead>
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
                <TableCell class="px-3 align-top">
                  <Badge :variant="stabilityVariant(plugin.stability)" class="text-xs">
                    {{ stabilityLabel(plugin.stability) }}
                  </Badge>
                </TableCell>
                <TableCell class="hidden lg:table-cell px-3 align-top font-mono text-xs text-muted-foreground max-w-[12rem] truncate">
                  {{ plugin.filePath || "-" }}
                </TableCell>
                <TableCell class="hidden xl:table-cell px-3 align-top text-xs text-muted-foreground tabular-nums whitespace-nowrap">
                  {{ formatLoadedAt(plugin.loadedAt) }}
                </TableCell>
                <TableCell class="px-3 align-top">
                  <Badge :variant="plugin.isSystem ? 'secondary' : 'outline'">
                    {{ plugin.isSystem ? $t("marketplace.pluginsPage.systemType") : $t("marketplace.pluginsPage.userType") }}
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
                      :title="$t('marketplace.pluginsPage.reload')"
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
                          :title="$t('common.delete')"
                        >
                          <Trash2 class="size-4 text-destructive" />
                        </Button>
                      </AlertDialogTrigger>
                      <AlertDialogContent>
                        <AlertDialogHeader>
                          <AlertDialogTitle>{{ $t("marketplace.pluginsPage.deletePluginTitle") }}</AlertDialogTitle>
                          <AlertDialogDescription>
                            {{ $t("marketplace.pluginsPage.deletePluginDescription", { name: plugin.displayName }) }}
                          </AlertDialogDescription>
                        </AlertDialogHeader>
                        <AlertDialogFooter>
                          <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
                          <AlertDialogAction
                            @click="handleDelete(plugin.key)"
                            :disabled="isDeleting"
                          >
                            <Loader2 v-if="isDeleting" class="size-4 mr-2 animate-spin" />
                            {{ $t("common.delete") }}
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
