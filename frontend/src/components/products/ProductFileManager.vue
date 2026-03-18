<script setup>
import { ref, reactive, computed, onMounted, onUnmounted, nextTick, watch } from "vue";
import { useStore } from "vuex";
import { useRoute, useRouter } from "vue-router";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useFileDialog } from "@vueuse/core";
import Button from "@/components/ui/button/Button.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import Input from "@/components/ui/input/Input.vue";
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
  Loader2,
  ChevronRight,
  Share2,
  Copy,
  Check,
  X,
  CornerLeftUp,
} from "lucide-vue-next";
import { http } from "@/main";
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

// Per-file upload tracking
const pendingUploads = ref([]);
let uploadIdCounter = 0;
const isUploading = computed(() => pendingUploads.value.some(u => u.status === 'pending' || u.status === 'uploading'));

// Inline folder creation (Windows Explorer style)
const inlineNewFolder = ref(false);
const inlineNewFolderName = ref("Новая папка");
const inlineNewFolderInput = ref(null);
const isCreatingFolder = ref(false);

// Inline rename
const inlineRenameTarget = ref(null); // { type: 'folder'|'file', id, name }
const inlineRenameInput = ref(null);

// Dialogs
const showDeleteFolder = ref(false);
const deleteFolderTarget = ref(null);
const deleteFolderRecursive = ref(false);

// Share dialog (works for both files and folders)
const showShareDialog = ref(false);
const shareTarget = ref(null);
const shareTtlHours = ref("168");
const shareUrl = ref("");
const isCreatingShare = ref(false);
const shareCopied = ref(false);


// ── Selection ──
// Keys like "folder:5", "file:12"
const selection = ref(new Set());
const lastClickedKey = ref(null);

function selKey(type, id) { return `${type}:${id}`; }

// Flat ordered list of all items in current view (for shift-range)
const allCurrentItems = computed(() => {
  const items = [];
  for (const f of currentSubfolders.value) items.push({ type: 'folder', id: f.id, item: f });
  for (const f of currentFiles.value) items.push({ type: 'file', id: f.id, item: f });
  return items;
});

function isSelected(type, id) { return selection.value.has(selKey(type, id)); }

function onItemClick(e, type, id) {
  if (inlineRenameTarget.value) {
    const rt = inlineRenameTarget.value;
    if (rt.type !== type || rt.id !== id) cancelInlineRename();
  }
  const key = selKey(type, id);

  if (e.shiftKey && lastClickedKey.value) {
    // Range select
    const items = allCurrentItems.value;
    const lastIdx = items.findIndex(i => selKey(i.type, i.id) === lastClickedKey.value);
    const curIdx = items.findIndex(i => selKey(i.type, i.id) === key);
    if (lastIdx >= 0 && curIdx >= 0) {
      const [from, to] = lastIdx < curIdx ? [lastIdx, curIdx] : [curIdx, lastIdx];
      const newSel = new Set(selection.value);
      for (let i = from; i <= to; i++) {
        newSel.add(selKey(items[i].type, items[i].id));
      }
      selection.value = newSel;
    }
  } else if (e.ctrlKey || e.metaKey) {
    // Toggle single
    const newSel = new Set(selection.value);
    if (newSel.has(key)) newSel.delete(key); else newSel.add(key);
    selection.value = newSel;
    lastClickedKey.value = key;
  } else {
    // Single select
    selection.value = new Set([key]);
    lastClickedKey.value = key;
  }
}

function clearSelection() {
  selection.value = new Set();
  lastClickedKey.value = null;
  if (inlineRenameTarget.value) cancelInlineRename();
}

const selectedItems = computed(() => {
  return allCurrentItems.value.filter(i => selection.value.has(selKey(i.type, i.id)));
});

// ── Editable path bar ──
const isEditingPath = ref(false);
const editPathValue = ref("");
const pathInputRef = ref(null);

const breadcrumbs = computed(() => {
  const items = [{ id: null, name: "Файлы" }];
  if (currentFolderId.value == null) return items;
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

const currentPathString = computed(() => {
  return breadcrumbs.value.map(c => c.name).join(" / ");
});

const parentFolderId = computed(() => {
  if (currentFolderId.value == null) return null;
  const cur = folders.value.find(f => f.id === currentFolderId.value);
  return cur ? (cur.parentFolderId ?? null) : null;
});

function startPathEdit() {
  editPathValue.value = currentPathString.value;
  isEditingPath.value = true;
  nextTick(() => {
    const el = pathInputRef.value;
    if (el) { el.focus(); el.select(); }
  });
}

function commitPathEdit() {
  isEditingPath.value = false;
  const parts = editPathValue.value.split("/").map(s => s.trim()).filter(Boolean);
  // First part should be root name "Файлы"
  if (parts.length === 0 || parts[0] === "Файлы") {
    // Walk the path from root
    let folderId = null;
    for (let i = 1; i < parts.length; i++) {
      const match = folders.value.find(
        f => (f.parentFolderId ?? null) === folderId && f.name.toLowerCase() === parts[i].toLowerCase()
      );
      if (!match) {
        displaySonnerError(`Папка "${parts[i]}" не найдена`);
        return;
      }
      folderId = match.id;
    }
    navigateToFolder(folderId);
  }
}

function cancelPathEdit() {
  isEditingPath.value = false;
}

// ── Computed lists ──
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
  clearSelection();
  router.replace({ query: { ...route.query, folder: folderId ?? undefined } });
}

// ── Drag & Drop (multi-aware) ──
const dragItems = ref([]); // [{type, id}, ...]
const dropTargetId = ref(null);
const externalDragCounter = ref(0);

function onDragStart(e, type, id) {
  const key = selKey(type, id);
  if (selection.value.has(key) && selection.value.size > 1) {
    // Drag all selected
    dragItems.value = selectedItems.value.map(i => ({ type: i.type, id: i.id }));
  } else {
    // Drag single item (and select it)
    dragItems.value = [{ type, id }];
    selection.value = new Set([key]);
    lastClickedKey.value = key;
  }
  e.dataTransfer.effectAllowed = "move";
  // Ghost text
  const count = dragItems.value.length;
  if (count > 1) {
    const ghost = document.createElement("div");
    ghost.textContent = `${count} элементов`;
    ghost.style.cssText = "position:fixed;top:-100px;padding:4px 12px;background:#333;color:#fff;border-radius:4px;font-size:13px";
    document.body.appendChild(ghost);
    e.dataTransfer.setDragImage(ghost, 0, 0);
    setTimeout(() => ghost.remove(), 0);
  }
}

function onDragEnd() {
  dragItems.value = [];
  dropTargetId.value = null;
}

function onFolderDragOver(e, folderId) {
  if (dragItems.value.length === 0) return;
  // Don't allow dropping a folder onto itself
  if (dragItems.value.some(d => d.type === "folder" && d.id === folderId)) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = "move";
  dropTargetId.value = folderId;
}

function onFolderDragLeave() {
  dropTargetId.value = null;
}

async function onDrop(e, targetFolderId) {
  if (dragItems.value.length === 0) return;
  e.preventDefault();
  e.stopPropagation();
  const items = [...dragItems.value];
  dragItems.value = [];
  dropTargetId.value = null;

  const fileIds = items.filter(i => i.type === 'file').map(i => i.id);
  const folderIds = items.filter(i => i.type === 'folder').map(i => i.id);

  await store.dispatch("bulkMove", {
    productId: props.productId,
    targetFolderId,
    fileIds,
    folderIds,
    onSuccess: () => {},
    onError: (error) => { displaySonnerError(error); },
  });

  if (fileIds.length > 0) emit("filesChanged");
  await loadFolders();
  clearSelection();
}

// External file drop (upload from desktop)
function onAreaDragEnter(e) {
  if (dragItems.value.length > 0) return;
  externalDragCounter.value++;
}

function onAreaDragOver(e) {
  if (dragItems.value.length > 0) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = "copy";
}

function onAreaDragLeave(e) {
  if (dragItems.value.length > 0) return;
  externalDragCounter.value--;
}

async function onAreaDrop(e) {
  externalDragCounter.value = 0;
  if (dragItems.value.length > 0) return;
  e.preventDefault();
  e.stopPropagation();
  const files = e.dataTransfer.files;
  if (!files || files.length === 0) return;
  await uploadFiles(files);
}

async function uploadFiles(fileList) {
  const filesToUpload = Array.from(fileList);
  if (filesToUpload.length === 0) return;

  const entries = filesToUpload.map((file) => {
    const entry = reactive({
      id: ++uploadIdCounter,
      name: file.name,
      size: file.size,
      percent: 0,
      status: 'pending',
      folderId: currentFolderId.value,
      abortController: new AbortController(),
      file,
    });
    pendingUploads.value.push(entry);
    return entry;
  });

  let anyDone = false;
  const concurrency = 3;

  async function uploadOne(entry) {
    if (entry.abortController.signal.aborted) return;
    entry.status = 'uploading';

    const formData = new FormData();
    formData.append('files', entry.file);

    const url = entry.folderId != null
      ? `/products/${props.productId}/files?folderId=${entry.folderId}`
      : `/products/${props.productId}/files`;

    try {
      await http({
        url,
        method: 'PUT',
        data: formData,
        headers: { 'Content-Type': 'multipart/form-data' },
        timeout: 60 * 60 * 1000,
        cache: false,
        signal: entry.abortController.signal,
        onUploadProgress: (e) => {
          if (e.total) entry.percent = Math.round((e.loaded * 100) / e.total);
        },
      });
      entry.status = 'done';
      anyDone = true;
    } catch (error) {
      if (error?.code === 'ERR_CANCELED' || entry.abortController.signal.aborted) {
        entry.status = 'error';
      } else {
        entry.status = 'error';
        displaySonnerError(error);
      }
    }
  }

  // Upload with concurrency limit
  const queue = [...entries];
  const running = new Set();

  await new Promise((resolve) => {
    function next() {
      if (queue.length === 0 && running.size === 0) { resolve(); return; }
      while (running.size < concurrency && queue.length > 0) {
        const entry = queue.shift();
        const p = uploadOne(entry).then(() => { running.delete(p); next(); });
        running.add(p);
      }
    }
    next();
  });

  // Remove all entries at once after all uploads complete
  const entryIds = new Set(entries.map(e => e.id));
  pendingUploads.value = pendingUploads.value.filter(u => !entryIds.has(u.id));

  if (anyDone) {
    emit("filesChanged");
    loadFolders();
  }
}

function cancelAllUploads() {
  for (const entry of pendingUploads.value) {
    if (entry.status === 'pending' || entry.status === 'uploading') {
      entry.abortController.abort();
      entry.status = 'error';
    }
  }
  pendingUploads.value = [];
}

function cancelUpload(entryId) {
  const entry = pendingUploads.value.find(u => u.id === entryId);
  if (entry) {
    entry.abortController.abort();
    entry.status = 'error';
    pendingUploads.value = pendingUploads.value.filter(u => u.id !== entryId);
  }
}

const currentPendingUploads = computed(() => {
  return pendingUploads.value.filter(
    u => (u.folderId ?? null) === currentFolderId.value && (u.status === 'pending' || u.status === 'uploading' || u.status === 'done')
  );
});

// Context menu
const contextMenu = ref({ show: false, x: 0, y: 0, type: null, item: null });
const contextMenuEl = ref(null);

function onContextMenu(e, type, item) {
  e.preventDefault();
  e.stopPropagation();
  // If right-clicking a non-selected item, select it
  if (type !== 'area' && item) {
    const key = selKey(type, type === 'folder' ? item.id : item.id);
    if (!selection.value.has(key)) {
      selection.value = new Set([key]);
      lastClickedKey.value = key;
    }
  }
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

function startInlineCreate() {
  if (inlineNewFolder.value) return;
  inlineNewFolderName.value = "Новая папка";
  inlineNewFolder.value = true;
  nextTick(() => {
    const el = inlineNewFolderInput.value;
    if (el) { el.focus(); el.select(); }
  });
}

async function commitInlineCreate() {
  const name = inlineNewFolderName.value.trim();
  if (!name || isCreatingFolder.value) {
    inlineNewFolder.value = false;
    return;
  }
  isCreatingFolder.value = true;
  await store.dispatch("createProductFolder", {
    productId: props.productId,
    name,
    parentFolderId: currentFolderId.value,
    onSuccess: (response) => {
      folders.value.push(response.data.value);
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });
  isCreatingFolder.value = false;
  inlineNewFolder.value = false;
}

function cancelInlineCreate() {
  inlineNewFolder.value = false;
}

function startInlineRename(type, item) {
  const name = type === 'folder' ? item.name : extractFileName(item);
  inlineRenameTarget.value = { type, id: item.id, name, originalName: name };
  nextTick(() => {
    const raw = inlineRenameInput.value;
    const el = Array.isArray(raw) ? raw[0] : raw;
    if (el) {
      el.focus();
      if (type === 'file') {
        const dotIdx = name.lastIndexOf('.');
        el.setSelectionRange(0, dotIdx > 0 ? dotIdx : name.length);
      } else {
        el.select();
      }
    }
  });
}

async function commitInlineRename() {
  const target = inlineRenameTarget.value;
  if (!target) return;
  const newName = target.name.trim();
  inlineRenameTarget.value = null;
  if (!newName || newName === target.originalName) return;

  if (target.type === 'folder') {
    await store.dispatch("renameProductFolder", {
      productId: props.productId,
      folderId: target.id,
      name: newName,
      onSuccess: (response) => {
        const f = folders.value.find((f) => f.id === target.id);
        if (f) f.name = response.data.value.name;
      },
      onError: (error) => { displaySonnerError(error); },
    });
  } else {
    await store.dispatch("renameProductFile", {
      productId: props.productId,
      fileId: target.id,
      name: newName,
      onSuccess: () => { emit("filesChanged"); },
      onError: (error) => { displaySonnerError(error); },
    });
  }
}

function cancelInlineRename() {
  inlineRenameTarget.value = null;
}

function handleF2Rename() {
  if (selection.value.size !== 1) return;
  const key = [...selection.value][0];
  const [type, idStr] = key.split(':');
  const id = parseInt(idStr);
  if (type === 'folder') {
    const folder = folders.value.find(f => f.id === id);
    if (folder) startInlineRename('folder', folder);
  } else {
    const file = props.files.find(f => f.id === id);
    if (file) startInlineRename('file', file);
  }
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
      const removeIds = [deleteFolderTarget.value.id];
      if (deleteFolderRecursive.value) {
        collectDescendantIds(deleteFolderTarget.value.id, removeIds);
      }
      folders.value = folders.value.filter((f) => !removeIds.includes(f.id));
      displaySonnerSuccess("Папка удалена.");
      if (deleteFolderRecursive.value) emit("filesChanged");
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



function openShareDialog(type, item) {
  shareTarget.value = { type, item };
  shareTtlHours.value = "168";
  shareUrl.value = "";
  shareCopied.value = false;
  showShareDialog.value = true;
}

async function createShareLink() {
  if (!shareTarget.value || isCreatingShare.value) return;
  isCreatingShare.value = true;

  if (shareTarget.value.type === 'file') {
    await store.dispatch("shareFile", {
      fileId: shareTarget.value.item.id,
      ttlHours: parseInt(shareTtlHours.value),
      onSuccess: (response) => {
        shareUrl.value = `${window.location.origin}${response.data.value.url}`;
      },
      onError: (error) => { displaySonnerError(error); },
    });
  } else {
    await store.dispatch("shareFolder", {
      productId: props.productId,
      folderId: shareTarget.value.item?.id ?? null,
      ttlHours: parseInt(shareTtlHours.value),
      onSuccess: (response) => {
        shareUrl.value = `${window.location.origin}${response.data.value.url}`;
      },
      onError: (error) => { displaySonnerError(error); },
    });
  }

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

const isDownloadingArchive = ref(false);
const downloadingFolderId = ref(null);

async function downloadFolderArchive(folderId) {
  if (isDownloadingArchive.value) return;
  isDownloadingArchive.value = true;
  downloadingFolderId.value = folderId;
  displaySonnerSuccess("Формируется архив...");
  try {
    const id = folderId ?? 'root';
    const response = await http({
      url: `/products/${props.productId}/folders/${id}/download`,
      method: 'GET',
      responseType: 'blob',
      timeout: 10 * 60 * 1000,
    });
    const disposition = response.headers?.['content-disposition'] || '';
    const match = disposition.match(/filename\*=UTF-8''(.+)/);
    const filename = match ? decodeURIComponent(match[1]) : 'archive.zip';
    const url = URL.createObjectURL(response.data);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isDownloadingArchive.value = false;
    downloadingFolderId.value = null;
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

function onKeyDown(e) {
  if (e.key === 'F2') {
    e.preventDefault();
    handleF2Rename();
  }
}

onMounted(() => {
  loadFolders();
  document.addEventListener('keydown', onKeyDown);
});

onUnmounted(() => {
  document.removeEventListener('keydown', onKeyDown);
});

// Reload folder counts when files list changes (e.g. file deleted by parent)
watch(() => props.files.length, () => {
  loadFolders();
});

watch(() => props.productId, () => {
  currentFolderId.value = null;
  folders.value = [];
  clearSelection();
  router.replace({ query: { ...route.query, folder: undefined } });
  loadFolders();
});
</script>

<template>
  <div class="flex flex-col h-full">
    <!-- Path bar (Windows Explorer style) -->
    <div
      class="flex items-center gap-2 text-sm min-w-0 h-11 px-4 border-b cursor-text"
      @click.self="startPathEdit"
    >
      <FolderOpen class="size-4 shrink-0" />
      <template v-if="isEditingPath">
        <input
          ref="pathInputRef"
          v-model="editPathValue"
          class="flex-1 bg-transparent outline-none text-sm h-full"
          @keydown.enter="commitPathEdit"
          @keydown.escape="cancelPathEdit"
          @blur="commitPathEdit"
        />
      </template>
      <template v-else>
        <template v-for="(crumb, i) in breadcrumbs" :key="crumb.id">
          <ChevronRight v-if="i > 0" class="size-3.5 shrink-0 text-muted-foreground" />
          <button
            @click.stop="navigateToFolder(crumb.id)"
            @dragover.prevent="dropTargetId = crumb.id"
            @dragleave="dropTargetId = null"
            @drop="onDrop($event, crumb.id)"
            :class="[
              'hover:underline truncate',
              i === breadcrumbs.length - 1 ? 'font-medium' : 'text-muted-foreground',
              dragItems.length > 0 && dropTargetId === crumb.id ? 'bg-accent rounded px-1' : ''
            ]"
          >
            {{ crumb.name }}
          </button>
        </template>
        <!-- Spacer to catch click for editing -->
        <span class="flex-1" @click="startPathEdit" />
      </template>
    </div>

    <!-- File list -->
    <div
      class="flex-1 overflow-auto relative"
      @contextmenu="onContextMenu($event, 'area', null)"
      @click.self="clearSelection"
      @dragenter="onAreaDragEnter"
      @dragover="onAreaDragOver"
      @dragleave="onAreaDragLeave"
      @drop="onAreaDrop"
    >
      <!-- External file drop overlay -->
      <div
        v-if="externalDragCounter > 0 && dragItems.length === 0"
        class="absolute inset-0 z-10 flex items-center justify-center bg-background/80 border-2 border-dashed border-primary/40 rounded-md pointer-events-none"
      >
        <div class="flex flex-col items-center gap-1 text-muted-foreground">
          <Upload class="size-8" />
          <p class="text-sm font-medium">Перетащите файлы для загрузки</p>
        </div>
      </div>

      <div v-if="currentSubfolders.length === 0 && currentFiles.length === 0 && !isLoadingFolders && !inlineNewFolder && currentPendingUploads.length === 0 && currentFolderId == null"
        class="flex flex-col items-center justify-center py-16 text-muted-foreground"
      >
        <Folder class="size-12 mb-3 opacity-40" />
        <p>Папка пуста</p>
        <p class="text-sm">Создайте папку или загрузите файлы</p>
      </div>

      <table v-if="currentFolderId != null || currentSubfolders.length > 0 || currentFiles.length > 0 || inlineNewFolder || currentPendingUploads.length > 0" class="w-full text-sm">
        <tbody>
          <!-- Go up row (..) -->
          <tr
            v-if="currentFolderId != null"
            class="border-b hover:bg-muted/50 cursor-pointer h-11 text-muted-foreground"
            @click="navigateToFolder(parentFolderId)"
            @dragover.prevent="dropTargetId = parentFolderId"
            @dragleave="dropTargetId = null"
            @drop="onDrop($event, parentFolderId)"
            :class="{ 'bg-accent ring-2 ring-primary/30': dragItems.length > 0 && dropTargetId === parentFolderId }"
          >
            <td class="pl-4 pr-3 w-8">
              <CornerLeftUp class="size-4" />
            </td>
            <td>..</td>
            <td></td>
            <td></td>
            <td class="pr-3 w-10"></td>
          </tr>

          <!-- Inline new folder row -->
          <tr v-if="inlineNewFolder" class="border-b bg-muted/30 h-11">
            <td class="pl-4 pr-3 w-8">
              <Loader2 v-if="isCreatingFolder" class="size-4 text-muted-foreground animate-spin" />
              <Folder v-else class="size-4 text-muted-foreground" />
            </td>
            <td colspan="3">
              <input
                ref="inlineNewFolderInput"
                v-model="inlineNewFolderName"
                maxlength="255"
                class="w-full bg-transparent outline-none border border-primary/40 rounded px-1.5 py-0.5 text-sm select-text"
                @keydown.enter="commitInlineCreate"
                @keydown.escape="cancelInlineCreate"
                @blur="commitInlineCreate"
                :disabled="isCreatingFolder"
              />
            </td>
            <td class="pr-3 w-10"></td>
          </tr>

          <!-- Folders -->
          <tr
            v-for="folder in currentSubfolders"
            :key="'f-' + folder.id"
            :draggable="!(inlineRenameTarget && inlineRenameTarget.type === 'folder' && inlineRenameTarget.id === folder.id)"
            @dragstart="onDragStart($event, 'folder', folder.id)"
            @dragend="onDragEnd"
            @dragover="onFolderDragOver($event, folder.id)"
            @dragleave="onFolderDragLeave"
            @drop="onDrop($event, folder.id)"
            @contextmenu="onContextMenu($event, 'folder', folder)"
            @click.exact="onItemClick($event, 'folder', folder.id)"
            @click.shift.exact="onItemClick($event, 'folder', folder.id)"
            @click.ctrl.exact="onItemClick($event, 'folder', folder.id)"
            @click.meta.exact="onItemClick($event, 'folder', folder.id)"
            @dblclick="navigateToFolder(folder.id)"
            :class="[
              'group border-b cursor-pointer h-11 select-none',
              isSelected('folder', folder.id) ? 'bg-accent' : 'hover:bg-muted/50',
              dropTargetId === folder.id ? 'bg-accent ring-2 ring-primary/30' : ''
            ]"
          >
            <td class="pl-4 pr-3 w-8">
              <Loader2 v-if="isDownloadingArchive && downloadingFolderId === folder.id" class="size-4 text-muted-foreground animate-spin" />
              <Folder v-else class="size-4 text-muted-foreground" />
            </td>
            <td>
              <input
                v-if="inlineRenameTarget && inlineRenameTarget.type === 'folder' && inlineRenameTarget.id === folder.id"
                ref="inlineRenameInput"
                v-model="inlineRenameTarget.name"
                maxlength="255"
                class="w-full bg-transparent outline-none border border-primary/40 rounded px-1.5 py-0.5 text-sm select-text"
                @keydown.enter="commitInlineRename"
                @keydown.escape="cancelInlineRename"
                @blur="commitInlineRename"
                @click.stop
                @dblclick.stop
                @mousedown.stop
                @dragstart.stop.prevent
              />
              <span v-else>{{ folder.name }}</span>
            </td>
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
            :draggable="!(inlineRenameTarget && inlineRenameTarget.type === 'file' && inlineRenameTarget.id === file.id)"
            @dragstart="onDragStart($event, 'file', file.id)"
            @dragend="onDragEnd"
            @contextmenu="onContextMenu($event, 'file', file)"
            @click.exact="onItemClick($event, 'file', file.id)"
            @click.shift.exact="onItemClick($event, 'file', file.id)"
            @click.ctrl.exact="onItemClick($event, 'file', file.id)"
            @click.meta.exact="onItemClick($event, 'file', file.id)"
            :class="[
              'group border-b h-11 select-none',
              isSelected('file', file.id) ? 'bg-accent' : 'hover:bg-muted/50',
            ]"
          >
            <td class="pl-4 pr-3 w-8">
              <FileIcon class="size-4 text-muted-foreground" />
            </td>
            <td>
              <input
                v-if="inlineRenameTarget && inlineRenameTarget.type === 'file' && inlineRenameTarget.id === file.id"
                ref="inlineRenameInput"
                v-model="inlineRenameTarget.name"
                maxlength="255"
                class="w-full bg-transparent outline-none border border-primary/40 rounded px-1.5 py-0.5 text-sm select-text"
                @keydown.enter="commitInlineRename"
                @keydown.escape="cancelInlineRename"
                @blur="commitInlineRename"
                @click.stop
                @dblclick.stop
                @mousedown.stop
                @dragstart.stop.prevent
              />
              <span v-else>{{ extractFileName(file) }}</span>
            </td>
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

          <!-- Uploading files -->
          <tr
            v-for="entry in currentPendingUploads"
            :key="'upload-' + entry.id"
            class="border-b h-11"
            :class="entry.status === 'done' ? 'bg-success/5' : 'animate-pulse bg-muted/40'"
          >
            <td class="pl-4 pr-3 w-8">
              <Loader2 v-if="entry.status === 'uploading'" class="size-4 text-muted-foreground animate-spin" />
              <Check v-else-if="entry.status === 'done'" class="size-4 text-success" />
              <FileIcon v-else class="size-4 text-muted-foreground" />
            </td>
            <td class="text-muted-foreground relative">
              <span>{{ entry.name }}</span>
              <div
                v-if="entry.status !== 'done'"
                class="absolute bottom-0 left-0 right-0 h-0.5"
              >
                <div
                  class="h-full bg-primary/40 transition-all duration-300 ease-out"
                  :style="{ width: entry.percent + '%' }"
                />
              </div>
            </td>
            <td class="text-muted-foreground text-xs tabular-nums">
              {{ entry.percent }}%
            </td>
            <td class="text-muted-foreground">{{ formatBytes(entry.size) }}</td>
            <td class="pr-3">
              <Button
                v-if="entry.status !== 'done'"
                variant="ghost"
                size="icon"
                class="size-7"
                @click.stop="cancelUpload(entry.id)"
              >
                <X class="size-3.5" />
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
              @click="downloadFolderArchive(contextMenu.item.id); closeContextMenu()"
            >
              <ArrowDownToLine class="size-4 text-muted-foreground" />
              Скачать архив
            </button>
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="openShareDialog('folder', contextMenu.item); closeContextMenu()"
            >
              <Share2 class="size-4 text-muted-foreground" />
              Поделиться
            </button>
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="startInlineRename('folder', contextMenu.item); closeContextMenu()"
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
              @click="openShareDialog('file', contextMenu.item); closeContextMenu()"
            >
              <Share2 class="size-4 text-muted-foreground" />
              Поделиться
            </button>
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="startInlineRename('file', contextMenu.item); closeContextMenu()"
            >
              <Pencil class="size-4 text-muted-foreground" />
              Переименовать
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
              @click="startInlineCreate(); closeContextMenu()"
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
            <div class="bg-border -mx-1 my-1 h-px" />
            <button
              class="relative flex w-full cursor-default items-center gap-2 rounded-sm px-2 py-1.5 text-sm select-none hover:bg-accent hover:text-accent-foreground"
              @click="downloadFolderArchive(currentFolderId); closeContextMenu()"
            >
              <ArrowDownToLine class="size-4 text-muted-foreground" />
              Скачать архив
            </button>
          </template>
        </div>
      </div>
    </Teleport>

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

    <!-- Share dialog (file or folder) -->
    <Dialog :open="showShareDialog" @update:open="showShareDialog = $event">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ shareTarget?.type === 'folder' ? 'Поделиться папкой' : 'Поделиться файлом' }}</DialogTitle>
        </DialogHeader>
        <p class="text-sm text-muted-foreground mb-2">
          {{ shareTarget?.type === 'file' ? extractFileName(shareTarget.item) : (shareTarget?.item?.name ?? 'Все файлы') }}
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
              <Check v-if="shareCopied" class="size-4 text-success" />
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
      <Button variant="outline" size="sm" @click="startInlineCreate">
        <Plus class="size-4 mr-1" />
        Папка
      </Button>
      <Button size="sm" @click="openFileDialog">
        <Upload class="size-4 mr-1" />
        Загрузить
      </Button>
      <Button v-if="isUploading" variant="destructive" size="sm" @click="cancelAllUploads">
        <X class="size-4 mr-1" />
        Отменить
      </Button>
      <Button variant="outline" size="sm" @click="downloadFolderArchive(currentFolderId)" :disabled="isDownloadingArchive">
        <Loader2 v-if="isDownloadingArchive && downloadingFolderId === currentFolderId" class="size-4 animate-spin mr-1" />
        <ArrowDownToLine v-else class="size-4 mr-1" />
        Скачать архив
      </Button>
      <span v-if="selection.size > 1" class="text-xs text-muted-foreground ml-auto">
        Выбрано: {{ selection.size }}
      </span>
    </div>
  </div>
</template>
