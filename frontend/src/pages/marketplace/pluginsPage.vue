<script>
import { Spinner } from "@/components/ui/spinner";
import PageSection from "@/components/pageSection.vue";

import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import {
  Plus,
  Puzzle,
  Trash2,
  RefreshCw,
  Upload,
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
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";

export default {
  setup() {
    const { formatDateTime: formatStoreDateTime, loadMemberships } =
      useStoreDateTime();
    loadMemberships();
    return { formatStoreDateTime };
  },
  components: {
    Spinner,
    PageSection,
    Button,
    Badge,
    AdapterIcon,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Plus,
    Puzzle,
    Trash2,
    RefreshCw,
    Upload,
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
          displaySonnerSuccess(
            this.$t("marketplace.pluginsPage.pluginUploaded"),
          );
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
          displaySonnerSuccess(
            this.$t("marketplace.pluginsPage.pluginDeleted"),
          );
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
          displaySonnerSuccess(
            this.$t("marketplace.pluginsPage.pluginReloaded"),
          );
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
  <div class="flex min-h-0 flex-1 flex-col">
    <div class="mb-4 flex shrink-0 flex-wrap items-center justify-end gap-2">
      <Dialog v-model:open="showUploadDialog">
        <DialogTrigger as-child>
          <Button>
            <Upload class="size-4 mr-2" />
            {{ $t("marketplace.pluginsPage.uploadPlugin") }}
          </Button>
        </DialogTrigger>
        <DialogContent size="lg" class="gap-0 p-0">
          <DialogHeader variant="bar">
            <DialogTitle>
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
              <p
                class="mt-1 max-w-sm text-xs leading-relaxed text-muted-foreground"
              >
                {{ $t("marketplace.pluginsPage.dropzoneHint") }}
              </p>
              <span
                class="mt-4 inline-flex h-8 items-center rounded-md border bg-background px-3 text-xs font-medium"
              >
                {{ $t("marketplace.pluginsPage.chooseFile") }}
              </span>
            </button>

            <!-- Selected: payload card -->
            <InsetGroup v-else>
              <InsetGroupItem as="div">
                <template #leading>
                  <FileCode2 class="size-4" />
                </template>
                <p class="truncate">{{ selectedFile.name }}</p>
                <p
                  class="mt-0.5 text-xs font-normal tabular-nums text-muted-foreground"
                >
                  .dll · {{ selectedFileSizeLabel }}
                </p>
                <template #trailing>
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    class="size-8 text-muted-foreground"
                    :title="$t('marketplace.pluginsPage.chooseAnother')"
                    :disabled="isUploading"
                    @click="resetUploadForm"
                  >
                    <X class="size-4" />
                  </Button>
                </template>
              </InsetGroupItem>
            </InsetGroup>
            <p class="mt-2 px-1 text-xs text-muted-foreground">
              {{ $t("marketplace.pluginsPage.readyToInstall") }}
              ·
              <button
                type="button"
                class="font-medium text-foreground underline-offset-2 hover:underline"
                :disabled="isUploading"
                @click="openFilePicker"
              >
                {{ $t("marketplace.pluginsPage.chooseAnother") }}
              </button>
            </p>

            <p class="text-xs leading-relaxed text-muted-foreground">
              {{ $t("marketplace.pluginsPage.sdkInterfaceHintBefore") }}
              <code
                class="rounded-sm bg-muted px-1 py-0.5 font-mono text-[11px] text-foreground"
                >IMarketplacePlugin</code
              >
              {{ $t("marketplace.pluginsPage.sdkInterfaceHintAfter") }}
            </p>
          </div>

          <DialogFooter variant="bar" class="sm:justify-between">
            <DialogClose as-child>
              <Button variant="outline" :disabled="isUploading">
                {{ $t("common.cancel") }}
              </Button>
            </DialogClose>
            <Button
              :disabled="isUploading || !selectedFile"
              @click="handleUpload"
            >
              <Spinner v-if="isUploading" class="mr-2" />
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

    <section v-if="availableFromRegistry.length" class="mb-8">
      <InsetGroupLabel>{{
        $t("marketplace.pluginsPage.registryTitle")
      }}</InsetGroupLabel>
      <p class="mb-2 px-1 text-xs text-muted-foreground">
        {{ $t("marketplace.pluginsPage.registryDescription") }}
      </p>
      <div class="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow class="hover:bg-transparent">
              <TableHead class="px-3">{{
                $t("marketplace.pluginsPage.plugin")
              }}</TableHead>
              <TableHead class="w-40 px-3">{{
                $t("marketplace.pluginsPage.key")
              }}</TableHead>
              <TableHead class="w-24 px-3">{{
                $t("marketplace.pluginsPage.version")
              }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="plugin in availableFromRegistry" :key="plugin.key">
              <TableCell class="max-w-0 px-3 font-medium">
                <div class="flex min-w-0 items-center gap-2">
                  <AdapterIcon
                    :adapter-key="plugin.key"
                    class="size-4 shrink-0"
                  />
                  <span class="truncate">{{ plugin.displayName }}</span>
                </div>
              </TableCell>
              <TableCell
                class="text-muted-foreground max-w-0 px-3 font-mono text-xs"
              >
                <span class="block truncate">{{ plugin.key }}</span>
              </TableCell>
              <TableCell class="px-3">
                <Badge variant="outline" class="text-xs tabular-nums">
                  v{{ plugin.latestVersion || "-" }}
                </Badge>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </section>

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

      <div class="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow class="hover:bg-transparent">
              <TableHead class="px-3">{{
                $t("marketplace.pluginsPage.plugin")
              }}</TableHead>
              <TableHead class="hidden w-40 px-3 md:table-cell">{{
                $t("marketplace.pluginsPage.key")
              }}</TableHead>
              <TableHead class="w-24 px-3">{{
                $t("marketplace.pluginsPage.version")
              }}</TableHead>
              <TableHead class="w-32 px-3">{{ $t("common.status") }}</TableHead>
              <TableHead class="hidden w-40 px-3 lg:table-cell">{{
                $t("marketplace.pluginsPage.file")
              }}</TableHead>
              <TableHead class="hidden w-40 px-3 xl:table-cell">{{
                $t("marketplace.pluginsPage.loaded")
              }}</TableHead>
              <TableHead class="w-28 px-3">{{
                $t("marketplace.pluginsPage.type")
              }}</TableHead>
              <TableHead class="w-24 px-2" />
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="plugin in plugins" :key="plugin.key" class="group">
              <TableCell class="max-w-0 px-3 align-top">
                <div class="flex min-w-0 items-start gap-2.5">
                  <AdapterIcon
                    :adapter-key="plugin.key"
                    class="mt-0.5 size-4 shrink-0"
                  />
                  <div class="min-w-0">
                    <div
                      class="truncate font-medium"
                      :title="plugin.displayName"
                    >
                      {{ plugin.displayName }}
                    </div>
                    <div
                      v-if="plugin.description"
                      class="text-xs text-muted-foreground mt-0.5 line-clamp-2"
                    >
                      {{ plugin.description }}
                    </div>
                    <code
                      class="text-xs text-muted-foreground font-mono mt-1 block md:hidden"
                    >
                      {{ plugin.key }}
                    </code>
                  </div>
                </div>
              </TableCell>
              <TableCell
                class="text-muted-foreground hidden max-w-0 px-3 align-top font-mono text-xs md:table-cell"
              >
                <span class="block truncate">{{ plugin.key }}</span>
              </TableCell>
              <TableCell class="px-3 align-top">
                <Badge variant="outline" class="text-xs tabular-nums">
                  v{{ plugin.version }}
                </Badge>
              </TableCell>
              <TableCell class="px-3 align-top">
                <Badge
                  :variant="stabilityVariant(plugin.stability)"
                  class="text-xs"
                >
                  {{ stabilityLabel(plugin.stability) }}
                </Badge>
              </TableCell>
              <TableCell
                class="hidden lg:table-cell px-3 align-top font-mono text-xs text-muted-foreground max-w-[12rem] truncate"
              >
                {{ plugin.filePath || "-" }}
              </TableCell>
              <TableCell
                class="hidden xl:table-cell px-3 align-top text-xs text-muted-foreground tabular-nums whitespace-nowrap"
              >
                {{ formatLoadedAt(plugin.loadedAt) }}
              </TableCell>
              <TableCell class="px-3 align-top">
                <Badge :variant="plugin.isSystem ? 'secondary' : 'outline'">
                  {{
                    plugin.isSystem
                      ? $t("marketplace.pluginsPage.systemType")
                      : $t("marketplace.pluginsPage.userType")
                  }}
                </Badge>
              </TableCell>
              <TableCell class="px-2 align-top">
                <div
                  class="flex items-center justify-end gap-1 opacity-0 group-hover:opacity-100 transition-opacity"
                >
                  <Button
                    variant="ghost"
                    size="icon"
                    class="size-8"
                    @click="handleReload(plugin.key)"
                    :disabled="isReloading === plugin.key"
                    :title="$t('marketplace.pluginsPage.reload')"
                  >
                    <Spinner v-if="isReloading === plugin.key" />
                    <RefreshCw v-else class="size-4" />
                  </Button>
                  <AlertDialog v-if="!plugin.isSystem">
                    <AlertDialogTrigger as-child>
                      <Button
                        variant="ghost"
                        size="icon"
                        class="size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
                        :title="$t('common.delete')"
                      >
                        <Trash2 class="size-4" />
                      </Button>
                    </AlertDialogTrigger>
                    <AlertDialogContent>
                      <AlertDialogHeader>
                        <AlertDialogTitle>{{
                          $t("marketplace.pluginsPage.deletePluginTitle")
                        }}</AlertDialogTitle>
                        <AlertDialogDescription>
                          {{
                            $t(
                              "marketplace.pluginsPage.deletePluginDescription",
                              { name: plugin.displayName },
                            )
                          }}
                        </AlertDialogDescription>
                      </AlertDialogHeader>
                      <AlertDialogFooter>
                        <AlertDialogCancel>{{
                          $t("common.cancel")
                        }}</AlertDialogCancel>
                        <AlertDialogAction
                          variant="destructive"
                          @click="handleDelete(plugin.key)"
                          :disabled="isDeleting"
                        >
                          <Spinner v-if="isDeleting" class="mr-2" />
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
    </PageSection>
  </div>
</template>
