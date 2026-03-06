<script setup>
import { ref, computed, onMounted, nextTick } from "vue";
import { useStore } from "vuex";
import { useRoute, useRouter } from "vue-router";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useFileDialog } from "@vueuse/core";
import Button from "@/components/ui/button/Button.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Input from "@/components/ui/input/Input.vue";
import { Progress } from "@/components/ui/progress";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Folder,
  FolderOpen,
  FileIcon,
  Plus,
  Upload,
  Pencil,
  Trash2,
  ArrowDownToLine,
  FolderInput,
  Loader2,
  ChevronRight,
  Share2,
  Copy,
  Check,
} from "lucide-vue-next";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";

const props = defineProps({
  files: { type: Array, required: true },
  productId: { type: Number, required: true },
});

const emit = defineEmits(["deleteFile", "filesChanged"]);

const store = useStore();
const route = useRoute();
const router = useRouter();

// State
const folders = ref([]);
const initialFolder = route.query.folder ? parseInt(route.query.folder) : null;
const currentFolderId = ref(initialFolder);
const isLoadingFolders = ref(false);
const isUploading = ref(false);
const uploadProgress = ref({ loaded: 0, total: 0, percent: 0 });

// Dialogs
const showCreateFolder = ref(false);
const newFolderName = ref("");
const showRenameFolder = ref(false);
const renameFolderTarget = ref(null);
const renameFolderName = ref("");
const showDeleteFolder = ref(false);
const deleteFolderTarget = ref(null);
const deleteFolderRecursive = ref(false);
const showMoveFile = ref(false);
const moveFileTarget = ref(null);
const moveFileFolderId = ref("root");

// Share dialog
const showShareDialog = ref(false);
const shareFileTarget = ref(null);
const shareTtlHours = ref("168");
const shareUrl = ref("");
const isCreatingShare = ref(false);
const shareCopied = ref(false);

// Computed
const breadcrumbs = computed(() => {
  const items = [{ id: null, name: "Файлы" }];
  if (currentFolderId.value == null) return items;

  // Build path from root to current
  const path = [];
  let cur = currentFolderId.value;
  while (cur != null) {
    const folder = folders.value.find((f) => f.id === cur);
    if (!folder) break;
    path.unshift({ id: folder.id, name: folder.name });
    cur = folder.parentFolderId;
  }
  return [...items, ...path];
});

const currentSubfolders = computed(() => {
  return folders.value
    .filter((f) => (f.parentFolderId ?? null) === currentFolderId.value)
    .sort((a, b) => a.name.localeCompare(b.name));
});

const currentFiles = computed(() => {
  return props.files
    .filter((f) => (f.folderId ?? null) === currentFolderId.value)
    .sort((a, b) => {
      if (a.contentType < b.contentType) return -1;
      if (a.contentType > b.contentType) return 1;
      return 0;
    });
});

// Folder select options (flat list with indentation) for move dialog
const folderOptions = computed(() => {
  const result = [];
  function addChildren(parentId, depth) {
    const children = folders.value
      .filter((f) => (f.parentFolderId ?? null) === parentId)
      .sort((a, b) => a.name.localeCompare(b.name));
    for (const child of children) {
      // Don't show the file's current folder
      result.push({
        id: child.id,
        label: "\u00A0\u00A0".repeat(depth) + child.name,
        name: child.name,
      });
      addChildren(child.id, depth + 1);
    }
  }
  addChildren(null, 0);
  return result;
});

// File upload
const { open: openFileDialog, reset: resetFileDialog, onChange } = useFileDialog({
  accept: "*/*",
  multiple: true,
});

onChange((newFiles) => {
  if (!newFiles || newFiles.length === 0) return;
  uploadFiles(newFiles);
  resetFileDialog();
});

// Methods
async function loadFolders() {
  isLoadingFolders.value = true;
  await store.dispatch("getProductFolders", {
    productId: props.productId,
    onSuccess: (response) => {
      folders.value = response.data.value || [];
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });
  isLoadingFolders.value = false;
}

function navigateToFolder(folderId) {
  currentFolderId.value = folderId;
  router.replace({ query: { ...route.query, folder: folderId ?? undefined } });
}

// Drag & Drop
const dragItem = ref(null);
const dropTargetId = ref(null);
const externalDragCounter = ref(0);

function onDragStart(e, type, id) {
  dragItem.value = { type, id };
  e.dataTransfer.effectAllowed = "move";
}

function onDragEnd() {
  dragItem.value = null;
  dropTargetId.value = null;
}

function onFolderDragOver(e, folderId) {
  if (!dragItem.value) return; // external file drag — let area handle it
  if (dragItem.value.type === "folder" && dragItem.value.id === folderId) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = "move";
  dropTargetId.value = folderId;
}

function onFolderDragLeave() {
  dropTargetId.value = null;
}

async function onDrop(e, targetFolderId) {
  if (!dragItem.value) return; // external file drag — let area handle it
  e.preventDefault();
  e.stopPropagation();
  const { type, id } = dragItem.value;
  dragItem.value = null;
  dropTargetId.value = null;

  if (type === "file") {
    await store.dispatch("moveProductFile", {
      productId: props.productId,
      fileId: id,
      folderId: targetFolderId,
      onSuccess: () => { displaySonnerSuccess("Файл перемещён."); emit("filesChanged"); },
      onError: (error) => { displaySonnerError(error); },
    });
  } else if (type === "folder") {
    await store.dispatch("moveProductFolder", {
      productId: props.productId,
      folderId: id,
      newParentFolderId: targetFolderId,
      onSuccess: () => { displaySonnerSuccess("Папка перемещена."); loadFolders(); },
      onError: (error) => { displaySonnerError(error); },
    });
  }
}

// External file drop (upload from desktop)
function onAreaDragEnter(e) {
  if (dragItem.value) return;
  externalDragCounter.value++;
}

function onAreaDragOver(e) {
  if (dragItem.value) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = "copy";
}

function onAreaDragLeave(e) {
  if (dragItem.value) return;
  externalDragCounter.value--;
}

async function onAreaDrop(e) {
  externalDragCounter.value = 0;
  if (dragItem.value) return;
  e.preventDefault();
  e.stopPropagation();
  const files = e.dataTransfer.files;
  if (!files || files.length === 0) return;
  await uploadFiles(files);
}

async function uploadFiles(files) {
  if (isUploading.value) return;
  isUploading.value = true;
  uploadProgress.value = { loaded: 0, total: 0, percent: 0 };
  await store.dispatch("putProductFiles", {
    files,
    productId: props.productId,
    folderId: currentFolderId.value,
    onProgress: (progress) => {
      uploadProgress.value = progress;
    },
    onSuccess: () => {
      displaySonnerSuccess("Файлы загружены.");
      emit("filesChanged");
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });
  isUploading.value = false;
  uploadProgress.value = { loaded: 0, total: 0, percent: 0 };
}

// Context menu
const contextMenu = ref({ show: false, x: 0, y: 0, type: null, item: null });
const contextMenuEl = ref(null);

function onContextMenu(e, type, item) {
  e.preventDefault();
  e.stopPropagation();
  contextMenu.value = { show: true, x: e.clientX, y: e.clientY, type, item };
  nextTick(() => {
    if (!contextMenuEl.value) return;
    const rect = contextMenuEl.value.getBoundingClientRect();
    if (rect.right > window.innerWidth) {
      contextMenu.value.x = window.innerWidth - rect.width - 8;
    }
    if (rect.bottom > window.innerHeight) {
      contextMenu.value.y = window.innerHeight - rect.height - 8;
    }
  });
}

function closeContextMenu() {
  contextMenu.value.show = false;
}

function openCreateFolderDialog() {
  newFolderName.value = "";
  showCreateFolder.value = true;
}

async function createFolder() {
  if (!newFolderName.value.trim()) return;
  await store.dispatch("createProductFolder", {
    productId: props.productId,
    name: newFolderName.value.trim(),
    parentFolderId: currentFolderId.value,
    onSuccess: (response) => {
      folders.value.push(response.data.value);
      displaySonnerSuccess("Папка создана.");
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });
  showCreateFolder.value = false;
}

function openRenameDialog(folder) {
  renameFolderTarget.value = folder;
  renameFolderName.value = folder.name;
  showRenameFolder.value = true;
}

async function renameFolder() {
  if (!renameFolderName.value.trim() || !renameFolderTarget.value) return;
  await store.dispatch("renameProductFolder", {
    productId: props.productId,
    folderId: renameFolderTarget.value.id,
    name: renameFolderName.value.trim(),
    onSuccess: (response) => {
      const f = folders.value.find((f) => f.id === renameFolderTarget.value.id);
      if (f) f.name = response.data.value.name;
      displaySonnerSuccess("Папка переименована.");
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });
  showRenameFolder.value = false;
}

function openDeleteFolderDialog(folder) {
  deleteFolderTarget.value = folder;
  deleteFolderRecursive.value = false;
  showDeleteFolder.value = true;
}

async function deleteFolder() {
  if (!deleteFolderTarget.value) return;
  const isEmpty = deleteFolderTarget.value.fileCount === 0 && deleteFolderTarget.value.subfolderCount === 0;
  await store.dispatch("deleteProductFolder", {
    productId: props.productId,
    folderId: deleteFolderTarget.value.id,
    recursive: !isEmpty && deleteFolderRecursive.value,
    onSuccess: () => {
      // Remove from local state
      const removeIds = [deleteFolderTarget.value.id];
      if (deleteFolderRecursive.value) {
        collectDescendantIds(deleteFolderTarget.value.id, removeIds);
      }
      folders.value = folders.value.filter((f) => !removeIds.includes(f.id));
      displaySonnerSuccess("Папка удалена.");
      if (deleteFolderRecursive.value) {
        emit("filesChanged");
      }
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });
  showDeleteFolder.value = false;
}

function collectDescendantIds(parentId, result) {
  const children = folders.value.filter((f) => (f.parentFolderId ?? null) === parentId);
  for (const child of children) {
    result.push(child.id);
    collectDescendantIds(child.id, result);
  }
}

function openMoveFileDialog(file) {
  moveFileTarget.value = file;
  moveFileFolderId.value = file.folderId != null ? String(file.folderId) : "root";
  showMoveFile.value = true;
}

async function moveFile() {
  if (!moveFileTarget.value) return;
  const targetFolderId = moveFileFolderId.value === "root" ? null : parseInt(moveFileFolderId.value);
  await store.dispatch("moveProductFile", {
    productId: props.productId,
    fileId: moveFileTarget.value.id,
    folderId: targetFolderId,
    onSuccess: () => {
      displaySonnerSuccess("Файл перемещён.");
      emit("filesChanged");
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });
  showMoveFile.value = false;
}

function openShareDialog(file) {
  shareFileTarget.value = file;
  shareTtlHours.value = "168";
  shareUrl.value = "";
  shareCopied.value = false;
  showShareDialog.value = true;
}

async function createShareLink() {
  if (!shareFileTarget.value || isCreatingShare.value) return;
  isCreatingShare.value = true;
  await store.dispatch("shareFile", {
    fileId: shareFileTarget.value.id,
    ttlHours: parseInt(shareTtlHours.value),
    onSuccess: (response) => {
      const url = response.data.value.url;
      shareUrl.value = `${window.location.origin}${url}`;
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });
  isCreatingShare.value = false;
}

async function copyShareUrl() {
  try {
    await navigator.clipboard.writeText(shareUrl.value);
    shareCopied.value = true;
    displaySonnerSuccess("Ссылка скопирована");
    setTimeout(() => { shareCopied.value = false; }, 2000);
  } catch {
    displaySonnerError("Не удалось скопировать");
  }
}

function extractFileName(file) {
  return file.fileName || file.fullPath.split("/").pop();
}

function formatBytes(bytes, decimals = 2) {
  if (!+bytes) return "0 Bytes";
  const k = 1024;
  const dm = decimals < 0 ? 0 : decimals;
  const sizes = ["Bytes", "KiB", "MiB", "GiB", "TiB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(dm))} ${sizes[i]}`;
}

onMounted(() => {
  loadFolders();
});
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- Breadcrumbs -->
    <div class="flex items-center gap-1 text-sm min-w-0 overflow-hidden h-11 pl-4 border-b">
      <template v-for="(crumb, i) in breadcrumbs" :key="crumb.id">
        <ChevronRight v-if="i > 0" class="size-3.5 shrink-0 text-muted-foreground" />
        <button
          @click="navigateToFolder(crumb.id)"
          @dragover.prevent="dropTargetId = crumb.id"
          @dragleave="dropTargetId = null"
          @drop="onDrop($event, crumb.id)"
          :class="[
            'hover:underline truncate',
            i === breadcrumbs.length - 1 ? 'font-medium' : 'text-muted-foreground',
            dragItem && dropTargetId === crumb.id ? 'bg-accent rounded px-1' : ''
          ]"
        >
          {{ crumb.name }}
        </button>
      </template>
    </div>

    <!-- Upload progress -->
    <div v-if="isUploading" class="px-3 py-2 border-b space-y-1">
      <div class="flex justify-between text-xs text-muted-foreground">
        <span>Загрузка файлов...</span>
        <span>{{ uploadProgress.percent }}%</span>
      </div>
      <Progress :model-value="uploadProgress.percent" class="h-1.5" />
      <div class="text-xs text-muted-foreground text-right">
        {{ formatBytes(uploadProgress.loaded) }} / {{ formatBytes(uploadProgress.total) }}
      </div>
    </div>

    <!-- File list -->
    <div
      class="flex-1 overflow-auto relative"
      @contextmenu="onContextMenu($event, 'area', null)"
      @dragenter="onAreaDragEnter"
      @dragover="onAreaDragOver"
      @dragleave="onAreaDragLeave"
      @drop="onAreaDrop"
    >
      <!-- External file drop overlay -->
      <div
        v-if="externalDragCounter > 0 && !dragItem"
        class="absolute inset-0 z-10 flex items-center justify-center bg-background/80 border-2 border-dashed border-primary/40 rounded-md pointer-events-none"
      >
        <div class="flex flex-col items-center gap-1 text-muted-foreground">
          <Upload class="size-8" />
          <p class="text-sm font-medium">Перетащите файлы для загрузки</p>
        </div>
      </div>

      <div v-if="currentSubfolders.length === 0 && currentFiles.length === 0 && !isLoadingFolders"
        class="flex flex-col items-center justify-center py-16 text-muted-foreground"
      >
        <Folder class="size-12 mb-3 opacity-40" />
        <p>Папка пуста</p>
        <p class="text-sm">Создайте папку или загрузите файлы</p>
      </div>

      <table v-else class="w-full text-sm">
        <tbody>
          <!-- Folders -->
          <tr
            v-for="folder in currentSubfolders"
            :key="'f-' + folder.id"
            draggable="true"
            @dragstart="onDragStart($event, 'folder', folder.id)"
            @dragend="onDragEnd"
            @dragover="onFolderDragOver($event, folder.id)"
            @dragleave="onFolderDragLeave"
            @drop="onDrop($event, folder.id)"
            @contextmenu="onContextMenu($event, 'folder', folder)"
            :class="[
              'group border-b hover:bg-muted/50 cursor-pointer h-11',
              dropTargetId === folder.id ? 'bg-accent ring-2 ring-primary/30' : ''
            ]"
            @click="navigateToFolder(folder.id)"
          >
            <td class="pl-4 pr-3 w-8">
              <Folder class="size-4 text-muted-foreground" />
            </td>
            <td>{{ folder.name }}</td>
            <td class="text-muted-foreground">Папка</td>
            <td class="text-muted-foreground">
              {{ folder.fileCount }} {{ folder.fileCount === 1 ? 'файл' : 'файлов' }}
            </td>
            <td class="pr-3 w-10"></td>
          </tr>

          <!-- Files -->
          <tr
            v-for="file in currentFiles"
            :key="'file-' + file.id"
            draggable="true"
            @dragstart="onDragStart($event, 'file', file.id)"
            @dragend="onDragEnd"
            @contextmenu="onContextMenu($event, 'file', file)"
            class="group border-b hover:bg-muted/50 h-11"
          >
            <td class="pl-4 pr-3 w-8">
              <FileIcon class="size-4 text-muted-foreground" />
            </td>
            <td>{{ extractFileName(file) }}</td>
            <td>
              <Badge variant="secondary" class="font-normal">{{ file.contentType }}</Badge>
            </td>
            <td class="text-muted-foreground">{{ formatBytes(file.length) }}</td>
            <td class="pr-3">
              <Button
                variant="ghost"
                size="icon"
                class="size-7 opacity-0 group-hover:opacity-100"
                as="a"
                :href="file.fullPath"
                :download="!file.contentType.startsWith('image/') ? extractFileName(file) : undefined"
                target="_blank"
                @click.stop
              >
                <ArrowDownToLine class="size-3.5" />
              </Button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Context menu -->
    <Teleport to="body">
      <div v-if="contextMenu.show" class="fixed inset-0 z-50" @click="closeContextMenu" @contextmenu.prevent="closeContextMenu">
        <div
          ref="contextMenuEl"
          class="fixed z-50 min-w-[10rem] rounded-md border bg-popover p-1 text-popover-foreground shadow-md animate-in fade-in-0 zoom-in-95"
          :style="{ left: contextMenu.x + 'px', top: contextMenu.y + 'px' }"
        >
          <!-- Folder -->
          <template v-if="contextMenu.type === 'folder'">
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="navigateToFolder(contextMenu.item.id); closeContextMenu()"
            >
              <FolderOpen class="size-4 text-muted-foreground" />
              Открыть
            </button>
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="openRenameDialog(contextMenu.item); closeContextMenu()"
            >
              <Pencil class="size-4 text-muted-foreground" />
              Переименовать
            </button>
            <div class="bg-border -mx-1 my-1 h-px" />
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none text-destructive hover:bg-destructive/10"
              @click="openDeleteFolderDialog(contextMenu.item); closeContextMenu()"
            >
              <Trash2 class="size-4" />
              Удалить
            </button>
          </template>

          <!-- File -->
          <template v-else-if="contextMenu.type === 'file'">
            <a
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              :href="contextMenu.item.fullPath"
              target="_blank"
              @click="closeContextMenu()"
            >
              <ArrowDownToLine class="size-4 text-muted-foreground" />
              Скачать
            </a>
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="openShareDialog(contextMenu.item); closeContextMenu()"
            >
              <Share2 class="size-4 text-muted-foreground" />
              Поделиться
            </button>
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="openMoveFileDialog(contextMenu.item); closeContextMenu()"
            >
              <FolderInput class="size-4 text-muted-foreground" />
              Переместить
            </button>
            <div class="bg-border -mx-1 my-1 h-px" />
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none text-destructive hover:bg-destructive/10"
              @click.stop="emit('deleteFile', contextMenu.item.id); closeContextMenu()"
            >
              <Trash2 class="size-4" />
              Удалить
            </button>
          </template>

          <!-- Empty area -->
          <template v-else>
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="openCreateFolderDialog(); closeContextMenu()"
            >
              <Plus class="size-4 text-muted-foreground" />
              Создать папку
            </button>
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="openFileDialog(); closeContextMenu()"
            >
              <Upload class="size-4 text-muted-foreground" />
              Загрузить файлы
            </button>
          </template>
        </div>
      </div>
    </Teleport>

    <!-- Create folder dialog -->
    <Dialog :open="showCreateFolder" @update:open="showCreateFolder = $event">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Создать папку</DialogTitle>
        </DialogHeader>
        <Input
          v-model="newFolderName"
          placeholder="Название папки"
          maxlength="255"
          @keydown.enter="createFolder"
        />
        <DialogFooter>
          <Button variant="outline" @click="showCreateFolder = false">Отмена</Button>
          <Button @click="createFolder" :disabled="!newFolderName.trim()">Создать</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Rename folder dialog -->
    <Dialog :open="showRenameFolder" @update:open="showRenameFolder = $event">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Переименовать папку</DialogTitle>
        </DialogHeader>
        <Input
          v-model="renameFolderName"
          placeholder="Новое название"
          maxlength="255"
          @keydown.enter="renameFolder"
        />
        <DialogFooter>
          <Button variant="outline" @click="showRenameFolder = false">Отмена</Button>
          <Button @click="renameFolder" :disabled="!renameFolderName.trim()">Сохранить</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Delete folder dialog -->
    <AlertDialog :open="showDeleteFolder" @update:open="showDeleteFolder = $event">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            Удалить папку "{{ deleteFolderTarget?.name }}"?
          </AlertDialogTitle>
        </AlertDialogHeader>
        <div
          v-if="deleteFolderTarget && (deleteFolderTarget.fileCount > 0 || deleteFolderTarget.subfolderCount > 0)"
          class="flex items-center gap-2 py-2"
        >
          <Checkbox
            :checked="deleteFolderRecursive"
            @update:checked="deleteFolderRecursive = $event"
            id="recursive-delete"
          />
          <label for="recursive-delete" class="text-sm cursor-pointer">
            Удалить содержимое ({{ deleteFolderTarget.fileCount }} файлов, {{ deleteFolderTarget.subfolderCount }} подпапок)
          </label>
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>Отмена</AlertDialogCancel>
          <AlertDialogAction
            @click="deleteFolder"
            :disabled="deleteFolderTarget && (deleteFolderTarget.fileCount > 0 || deleteFolderTarget.subfolderCount > 0) && !deleteFolderRecursive"
          >
            Удалить
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <!-- Move file dialog -->
    <Dialog :open="showMoveFile" @update:open="showMoveFile = $event">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Переместить файл</DialogTitle>
        </DialogHeader>
        <p class="text-sm text-muted-foreground mb-2">
          {{ moveFileTarget ? extractFileName(moveFileTarget) : '' }}
        </p>
        <Select v-model="moveFileFolderId">
          <SelectTrigger>
            <SelectValue placeholder="Выберите папку" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="root">/ Корень</SelectItem>
            <SelectItem
              v-for="opt in folderOptions"
              :key="opt.id"
              :value="String(opt.id)"
            >
              {{ opt.label }}
            </SelectItem>
          </SelectContent>
        </Select>
        <DialogFooter>
          <Button variant="outline" @click="showMoveFile = false">Отмена</Button>
          <Button @click="moveFile">Переместить</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <!-- Share file dialog -->
    <Dialog :open="showShareDialog" @update:open="showShareDialog = $event">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Поделиться файлом</DialogTitle>
        </DialogHeader>
        <p class="text-sm text-muted-foreground mb-2">
          {{ shareFileTarget ? extractFileName(shareFileTarget) : '' }}
        </p>
        <template v-if="!shareUrl">
          <Select v-model="shareTtlHours">
            <SelectTrigger>
              <SelectValue placeholder="Срок действия" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="1">1 час</SelectItem>
              <SelectItem value="24">1 день</SelectItem>
              <SelectItem value="168">7 дней</SelectItem>
              <SelectItem value="720">30 дней</SelectItem>
            </SelectContent>
          </Select>
          <DialogFooter>
            <Button variant="outline" @click="showShareDialog = false">Отмена</Button>
            <Button @click="createShareLink" :disabled="isCreatingShare">
              <Loader2 v-if="isCreatingShare" class="size-4 animate-spin mr-1" />
              Создать ссылку
            </Button>
          </DialogFooter>
        </template>
        <template v-else>
          <div class="flex gap-2">
            <Input :model-value="shareUrl" readonly class="flex-1 text-xs" @click="$event.target.select()" />
            <Button variant="outline" size="icon" @click="copyShareUrl">
              <Check v-if="shareCopied" class="size-4 text-green-500" />
              <Copy v-else class="size-4" />
            </Button>
          </div>
          <DialogFooter>
            <Button variant="outline" @click="showShareDialog = false">Закрыть</Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>

    <!-- Sticky bottom actions -->
    <div class="sticky bottom-0 flex items-center gap-2 p-3 border-t bg-background rounded-b-xl">
      <Button variant="outline" size="sm" @click="openCreateFolderDialog">
        <Plus class="size-4 mr-1" />
        Папка
      </Button>
      <Button size="sm" @click="openFileDialog" :disabled="isUploading">
        <template v-if="!isUploading">
          <Upload class="size-4 mr-1" />
          Загрузить
        </template>
        <Loader2 v-else class="size-4 animate-spin" />
      </Button>
    </div>
  </div>
</template>
