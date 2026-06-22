<script setup>
import { ref, reactive, computed, onMounted, onUnmounted, nextTick, watch } from "vue";
import { useStore } from "vuex";
import { useRoute, useRouter } from "vue-router";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useFileDialog, useDebounceFn } from "@vueuse/core";
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
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { Tag } from "lucide-vue-next";

const props = defineProps({
  files: { type: Array, required: true },
  productId: { type: Number, default: null },
  productName: { type: String, default: "" },
  productSku: { type: String, default: "" },
  scope: { type: String, default: "product" },
  storageLabel: { type: String, default: "" },
});

const emit = defineEmits(["deleteFile", "filesChanged"]);

// Debounced emit for incremental file list refresh during multi-file uploads.
// Each completed file triggers this; 800ms debounce coalesces rapid completions
// into a single fetchProduct call.
const debouncedFilesChanged = useDebounceFn(() => emit("filesChanged"), 800);

const store = useStore();
const route = useRoute();
const router = useRouter();

const isStoreScope = computed(() => props.scope === "store");
const rootLabel = computed(() => isStoreScope.value ? "Хранилище" : "Файлы");
const subjectLabel = computed(() => {
  if (props.storageLabel) return props.storageLabel;
  if (isStoreScope.value) return "Общее хранилище магазина";
  return props.productSku
    ? `${props.productName || "Товар"} [${props.productSku}]`
    : (props.productName || `Товар #${props.productId}`);
});

function productRequired() {
  if (!props.productId) {
    throw new Error("productId is required for product file storage");
  }
  return props.productId;
}

function uploadFilesUrl(folderId) {
  if (isStoreScope.value) {
    return folderId != null
      ? `/store-storage/files?folderId=${folderId}`
      : "/store-storage/files";
  }
  const productId = productRequired();
  return folderId != null
    ? `/products/${productId}/files?folderId=${folderId}`
    : `/products/${productId}/files`;
}

function bulkMoveUrl() {
  return isStoreScope.value ? "/store-storage/bulk-move" : `/products/${productRequired()}/bulk-move`;
}

function foldersUrl(folderId = null, action = "") {
  if (isStoreScope.value) {
    if (folderId == null) return "/store-storage/folders";
    return `/store-storage/folders/${folderId}${action}`;
  }
  if (folderId == null) return `/products/${productRequired()}/folders`;
  return `/products/${productRequired()}/folders/${folderId}${action}`;
}

function renameFileUrl(fileId) {
  return isStoreScope.value
    ? `/store-storage/files/${fileId}/rename`
    : `/products/${productRequired()}/files/${fileId}/rename`;
}

function bulkDeleteFilesUrl() {
  return isStoreScope.value
    ? "/store-storage/files/bulk-delete"
    : `/products/${productRequired()}/files/bulk-delete`;
}

function shareFileUrl(fileId) {
  return isStoreScope.value ? `/store-storage/files/${fileId}/share` : `/files/${fileId}/share`;
}

function shareFolderUrl(folderId) {
  const id = folderId ?? "root";
  return isStoreScope.value
    ? `/store-storage/folders/${id}/share`
    : `/products/${productRequired()}/folders/${id}/share`;
}

function downloadFolderUrl(folderId) {
  const id = folderId ?? "root";
  return isStoreScope.value
    ? `/store-storage/folders/${id}/download`
    : `/products/${productRequired()}/folders/${id}/download`;
}

function fileTagUrl(fileId, tagId) {
  return isStoreScope.value
    ? `/store-storage/files/${fileId}/tags/${tagId}`
    : `/files/${fileId}/tags/${tagId}`;
}

function bulkTagUrl() {
  return isStoreScope.value ? "/store-storage/files/bulk-tag" : "/files/bulk-tag";
}

// Tag palette
const storeTags = ref([]);
const tagPopoverFileId = ref(null);
const newTagName = ref("");
const newTagColor = ref("Red");
// Values must match C# FileTagColor enum NAMES (JsonStringEnumConverter)
const TAG_COLORS = [
  { value: "Red", label: "Красный", bg: "bg-red-500", text: "text-white" },
  { value: "Blue", label: "Синий", bg: "bg-blue-500", text: "text-white" },
  { value: "Green", label: "Зелёный", bg: "bg-emerald-500", text: "text-white" },
  { value: "Yellow", label: "Жёлтый", bg: "bg-yellow-300", text: "text-yellow-900" },
  { value: "Orange", label: "Оранжевый", bg: "bg-orange-500", text: "text-white" },
  { value: "Purple", label: "Фиолетовый", bg: "bg-purple-500", text: "text-white" },
  { value: "Balck", label: "Чёрный", bg: "bg-neutral-800 dark:bg-neutral-300", text: "text-white dark:text-neutral-800" },
  { value: "White", label: "Серый", bg: "bg-neutral-200 dark:bg-neutral-600", text: "text-neutral-700 dark:text-neutral-100" },
];

function tagBgClass(colorValue) {
  return TAG_COLORS.find(c => c.value === colorValue)?.bg || "bg-muted";
}
function tagTextClass(colorValue) {
  return TAG_COLORS.find(c => c.value === colorValue)?.text || "text-foreground";
}

async function updateTagColor(tagId, newColor) {
  try {
    await http.patch(`/tags/${tagId}`, { color: newColor }, { cache: false });
    const tag = storeTags.value.find(t => t.id === tagId);
    if (tag) tag.color = newColor;
    // Update all local file refs
    for (const file of props.files) {
      const ft = file.tags?.find(t => t.id === tagId);
      if (ft) ft.color = newColor;
    }
  } catch (error) {
    displaySonnerError(error);
  }
}

const editingTagId = ref(null);
const renamingTagId = ref(null);
const renamingTagName = ref("");

function startRenameTag(tag) {
  renamingTagId.value = tag.id;
  renamingTagName.value = tag.name;
}

async function commitRenameTag() {
  if (!renamingTagId.value) return;
  const trimmed = renamingTagName.value.trim();
  if (!trimmed) { renamingTagId.value = null; return; }
  try {
    await http.patch(`/tags/${renamingTagId.value}`, { name: trimmed }, { cache: false });
    const tag = storeTags.value.find(t => t.id === renamingTagId.value);
    if (tag) tag.name = trimmed;
    // Update local file refs
    for (const file of props.files) {
      const ft = file.tags?.find(t => t.id === renamingTagId.value);
      if (ft) ft.name = trimmed;
    }
  } catch (error) {
    displaySonnerError(error);
  }
  renamingTagId.value = null;
}

async function loadStoreTags() {
  try {
    const res = await http.get("/tags", { cache: false });
    storeTags.value = res.data || [];
  } catch { storeTags.value = []; }
}

function fileHasTag(fileId, tagId) {
  const file = props.files.find(f => f.id === fileId);
  return file?.tags?.some(t => t.id === tagId) ?? false;
}

async function toggleFileTag(fileId, tagId) {
  const file = props.files.find(f => f.id === fileId);
  const tag = storeTags.value.find(t => t.id === tagId);
  const hadTag = fileHasTag(fileId, tagId);

  // Optimistic UI — update instantly
  if (hadTag) {
    if (file?.tags) file.tags = file.tags.filter(t => t.id !== tagId);
  } else if (file && tag) {
    if (!file.tags) file.tags = [];
    file.tags.push({ id: tag.id, name: tag.name, color: tag.color });
  }

  try {
    if (hadTag) {
      await http.delete(fileTagUrl(fileId, tagId), { cache: false });
    } else {
      await http.post(fileTagUrl(fileId, tagId), null, { cache: false });
    }
  } catch (error) {
    // Rollback
    if (hadTag) {
      if (file && tag) {
        if (!file.tags) file.tags = [];
        file.tags.push({ id: tag.id, name: tag.name, color: tag.color });
      }
    } else {
      if (file?.tags) file.tags = file.tags.filter(t => t.id !== tagId);
    }
    displaySonnerError(error);
  }
}

async function createAndAssignTag(fileId) {
  if (!newTagName.value.trim()) return;
  try {
    const res = await http.post("/tags", { name: newTagName.value.trim(), color: newTagColor.value }, { cache: false });
    const newTag = { id: res.data.Id ?? res.data.id, name: res.data.name, color: res.data.color };
    storeTags.value.push(newTag);
    await http.post(fileTagUrl(fileId, newTag.id), null, { cache: false });
    // Optimistic: add tag to file locally
    const file = props.files.find(f => f.id === fileId);
    if (file) {
      if (!file.tags) file.tags = [];
      file.tags.push({ id: newTag.id, name: newTag.name, color: newTag.color });
    }
    newTagName.value = "";
    newTagColor.value = "Red";
  } catch (error) {
    displaySonnerError(error);
  }
}

async function bulkToggleTag(tagId) {
  const files = selectedItems.value.filter(i => i.type === 'file');
  if (files.length === 0) return;

  const allHaveTag = files.every(f => f.item?.tags?.some(t => t.id === tagId));
  const tag = storeTags.value.find(t => t.id === tagId);

  // Snapshot for rollback: Map<fileId, tag[] copy>
  const snapshot = new Map(files.map(f => [f.id, f.item?.tags ? [...f.item.tags] : []]));

  // Optimistic UI — update instantly
  if (allHaveTag) {
    for (const f of files) {
      if (f.item?.tags) f.item.tags = f.item.tags.filter(t => t.id !== tagId);
    }
  } else if (tag) {
    for (const f of files) {
      if (!f.item?.tags?.some(t => t.id === tagId)) {
        if (!f.item.tags) f.item.tags = [];
        f.item.tags.push({ id: tag.id, name: tag.name, color: tag.color });
      }
    }
  }

  try {
    if (allHaveTag) {
      for (const f of files) {
        await http.delete(fileTagUrl(f.id, tagId), { cache: false });
      }
      displaySonnerSuccess(`Тэг убран с ${files.length} файлов`);
    } else {
      const fileIds = files.filter(f => !snapshot.get(f.id)?.some(t => t.id === tagId)).map(f => f.id);
      await http.post(bulkTagUrl(), { fileIds, tagId }, { cache: false });
      displaySonnerSuccess(`Тэг назначен ${fileIds.length} файлам`);
    }
  } catch (error) {
    // Rollback to snapshot
    for (const f of files) {
      if (f.item) f.item.tags = snapshot.get(f.id) ?? [];
    }
    displaySonnerError(error);
  }
}

function bulkAllHaveTag(tagId) {
  const files = selectedItems.value.filter(i => i.type === 'file');
  return files.length > 0 && files.every(f => f.item?.tags?.some(t => t.id === tagId));
}

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
  const items = [{ id: null, name: rootLabel.value }];
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
  // First part should be the current storage root name.
  if (parts.length === 0 || parts[0] === rootLabel.value) {
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
  try {
    const response = await http.get(foldersUrl(), { cache: false });
    folders.value = response.data?.value || response.data || [];
  } catch (error) {
    displaySonnerError(error);
  }
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

  try {
    await http.post(bulkMoveUrl(), { targetFolderId, fileIds, folderIds }, { cache: false });
  } catch (error) {
    displaySonnerError(error);
  }

  if (fileIds.length > 0) emit("filesChanged");
  await loadFolders();
  clearSelection();
}

// External file drop (upload from desktop)
function onAreaDragEnter(e) {
  if (dragItems.value.length > 0) return;
  e.preventDefault();
  externalDragCounter.value++;
}

function onAreaDragOver(e) {
  if (dragItems.value.length > 0) return;
  e.preventDefault();
  e.dataTransfer.dropEffect = "copy";
}

function onAreaDragLeave(e) {
  if (dragItems.value.length > 0) return;
  externalDragCounter.value = Math.max(0, externalDragCounter.value - 1);
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

  // Master abort controller — when triggered, cancels ALL in-flight uploads for this batch.
  const batchAbort = new AbortController();
  const totalFiles = entries.length;

  // Register operation in Vuex FIRST (before backend call) so the local version
  // with silent+onCancel wins the race against SignalR OperationStarted event.
  // ADD_OPERATION deduplicates by ID — whoever adds first keeps the entry.
  const opId = crypto.randomUUID().replace(/-/g, "");
  const uploadTitle = totalFiles === 1
    ? `Загрузка файла — ${subjectLabel.value}`
    : `Загрузка ${totalFiles} файлов — ${subjectLabel.value}`;

  store.dispatch("operations/addOperation", {
    id: opId,
    type: "file-upload",
    title: uploadTitle,
    progress: { total: totalFiles, completed: 0, failed: 0, pending: totalFiles },
    silent: true,
    onCancel: () => {
      batchAbort.abort();
      for (const entry of entries) {
        if (entry.status === 'pending' || entry.status === 'uploading') {
          entry.abortController.abort();
          entry.status = 'cancelled';
        }
      }
    },
  });

  // Then persist to backend (fire-and-forget — Vuex state is already set).
  // This creates the Operation DB record with author info for history.
  http.post("/operations/start", {
    operationId: opId,
    type: "file-upload",
    title: uploadTitle,
    totalItems: totalFiles,
  }, { cache: false }).catch(() => {});

  // First log entry: context about which product this operates on
  store.dispatch("operations/addLog", { id: opId, level: "info", message: subjectLabel.value });

  let completedFiles = 0;
  let failedFiles = 0;
  let cancelledFiles = 0;
  let anyDone = false;
  const concurrency = 3;

  function updateOpProgress() {
    store.dispatch("operations/updateProgress", {
      id: opId,
      progress: {
        total: totalFiles,
        completed: completedFiles,
        failed: failedFiles,
        pending: totalFiles - completedFiles - failedFiles,
      },
    });
  }

  async function uploadOne(entry) {
    if (entry.abortController.signal.aborted || batchAbort.signal.aborted) {
      entry.status = 'cancelled';
      cancelledFiles++;
      return;
    }
    entry.status = 'uploading';

    const formData = new FormData();
    formData.append('files', entry.file);

    const url = uploadFilesUrl(entry.folderId);

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
      completedFiles++;
      anyDone = true;
      // Remove from pendingUploads immediately so the user doesn't see
      // both the real file (from refreshed props.files) and the ✓ entry.
      pendingUploads.value = pendingUploads.value.filter(u => u.id !== entry.id);
      updateOpProgress();
      store.dispatch("operations/addLog", { id: opId, level: "success", message: entry.name });
      // Refresh file list incrementally — each completed file appears immediately.
      debouncedFilesChanged();
    } catch (error) {
      if (error?.code === 'ERR_CANCELED' || entry.abortController.signal.aborted || batchAbort.signal.aborted) {
        entry.status = 'cancelled';
        cancelledFiles++;
        updateOpProgress();
        store.dispatch("operations/addLog", { id: opId, level: "warning", message: `${entry.name}: отменено` });
      } else {
        entry.status = 'error';
        failedFiles++;
        updateOpProgress();
        store.dispatch("operations/addLog", { id: opId, level: "error", message: `${entry.name}: ${error?.response?.data?.message || error?.message || 'ошибка'}` });
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

  // Finish operation — but only if it wasn't already cancelled by stopOperation.
  // stopOperation calls FINISH_OPERATION(cancelled) before the loop ends;
  // we must not overwrite it.
  if (!batchAbort.signal.aborted) {
    const finalStatus = failedFiles > 0 ? (completedFiles > 0 ? "partial" : "failed") : "completed";
    const finalSummary = `Загружено: ${completedFiles}` + (failedFiles > 0 ? `, ошибок: ${failedFiles}` : "");
    store.dispatch("operations/finishOperation", {
      id: opId,
      status: finalStatus,
      summary: finalSummary,
    });
    try {
      await http.post(`/operations/${opId}/finish`, { status: finalStatus, summary: finalSummary }, { cache: false });
    } catch { /* ignore */ }
  } else {
    // Batch was cancelled — persist cancelled status to backend
    const summary = `Отменено. Загружено: ${completedFiles}, отменено: ${cancelledFiles}`;
    try {
      await http.post(`/operations/${opId}/finish`, { status: "cancelled", summary }, { cache: false });
    } catch { /* ignore */ }
  }

  // Remove remaining entries (failed/cancelled) after batch completes
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
  try {
    const response = await http.post(foldersUrl(), { name, parentFolderId: currentFolderId.value }, { cache: false });
    folders.value.push(response.data?.value || response.data);
  } catch (error) {
    displaySonnerError(error);
  }
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
    try {
      const response = await http.put(foldersUrl(target.id), { name: newName }, { cache: false });
      const f = folders.value.find((f) => f.id === target.id);
      if (f) f.name = (response.data?.value || response.data).name;
    } catch (error) {
      displaySonnerError(error);
    }
  } else {
    try {
      await http.patch(renameFileUrl(target.id), { name: newName }, { cache: false });
      emit("filesChanged");
    } catch (error) {
      displaySonnerError(error);
    }
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
  try {
    await http.delete(`${foldersUrl(deleteFolderTarget.value.id)}?recursive=${!!(!isEmpty && deleteFolderRecursive.value)}`, { cache: false });
    const removeIds = [deleteFolderTarget.value.id];
    if (deleteFolderRecursive.value) {
      collectDescendantIds(deleteFolderTarget.value.id, removeIds);
    }
    folders.value = folders.value.filter((f) => !removeIds.includes(f.id));
    displaySonnerSuccess("Папка удалена.");
    if (deleteFolderRecursive.value) emit("filesChanged");
  } catch (error) {
    displaySonnerError(error);
  }
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
    try {
      const response = await http.post(
        shareFileUrl(shareTarget.value.item.id),
        { ttlHours: parseInt(shareTtlHours.value) },
        { cache: false },
      );
      shareUrl.value = `${window.location.origin}${response.data?.value?.url || response.data?.url}`;
    } catch (error) {
      displaySonnerError(error);
    }
  } else {
    try {
      const response = await http.post(
        shareFolderUrl(shareTarget.value.item?.id ?? null),
        { ttlHours: parseInt(shareTtlHours.value) },
        { cache: false },
      );
      shareUrl.value = `${window.location.origin}${response.data?.value?.url || response.data?.url}`;
    } catch (error) {
      displaySonnerError(error);
    }
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
    const response = await http({
      url: downloadFolderUrl(folderId),
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
  if (e.key === 'Delete' || e.key === 'Backspace') {
    // Don't delete while renaming, editing path, or typing in an input
    if (inlineRenameTarget.value || isEditingPath.value || inlineNewFolder.value) return;
    const tag = document.activeElement?.tagName?.toLowerCase();
    if (tag === 'input' || tag === 'textarea' || document.activeElement?.isContentEditable) return;
    if (selection.value.size === 0) return;
    e.preventDefault();
    deleteSelected();
  }
  if (e.key === 'a' && (e.ctrlKey || e.metaKey)) {
    if (inlineRenameTarget.value || isEditingPath.value || inlineNewFolder.value) return;
    // Don't hijack Ctrl+A when user is typing in an input, textarea, or contenteditable
    const tag = document.activeElement?.tagName?.toLowerCase();
    if (tag === 'input' || tag === 'textarea' || document.activeElement?.isContentEditable) return;
    e.preventDefault();
    e.stopPropagation();
    // Select all items in current view
    const newSel = new Set();
    for (const item of allCurrentItems.value) newSel.add(selKey(item.type, item.id));
    selection.value = newSel;
  }
}

const isDeletingSelected = ref(false);
const showDeleteConfirm = ref(false);

async function deleteSelected() {
  const items = selectedItems.value;
  if (items.length === 0) return;
  showDeleteConfirm.value = true;
}

async function confirmDeleteSelected() {
  isDeletingSelected.value = true;
  const fileIds = selectedItems.value
    .filter(i => i.type === 'file')
    .map(i => i.id);
  const fileNames = selectedItems.value
    .filter(i => i.type === 'file')
    .map(i => i.item?.displayName || i.item?.name || `#${i.id}`);

  if (fileIds.length === 0) {
    isDeletingSelected.value = false;
    showDeleteConfirm.value = false;
    return;
  }

  await deleteFilesWithOperation(fileIds, fileNames);

  clearSelection();
  isDeletingSelected.value = false;
  showDeleteConfirm.value = false;
}

// Single file delete (from context menu)
async function deleteSingleFile(fileId) {
  const file = props.files.find(f => f.id === fileId);
  const name = file?.displayName || file?.name || `#${fileId}`;
  await deleteFilesWithOperation([fileId], [name]);
}

// Unified delete: registers operation, calls bulk-delete, logs results
async function deleteFilesWithOperation(fileIds, fileNames) {
  const count = fileIds.length;
  const opId = crypto.randomUUID().replace(/-/g, "");
  const title = count === 1
    ? `Удаление файла — ${subjectLabel.value}`
    : `Удаление ${count} файлов — ${subjectLabel.value}`;

  // Register in Vuex first (wins SignalR race), then backend
  store.dispatch("operations/addOperation", {
    id: opId,
    type: "file-delete",
    title,
    progress: { total: count, completed: 0, failed: 0, pending: count },
    silent: true,
  });
  http.post("/operations/start", {
    operationId: opId,
    type: "file-delete",
    title,
    totalItems: count,
  }, { cache: false }).catch(() => {});

  store.dispatch("operations/addLog", { id: opId, level: "info", message: subjectLabel.value });

  try {
    await http.post(bulkDeleteFilesUrl(), { fileIds }, { cache: false });

    // Log each file name
    for (const name of fileNames) {
      store.dispatch("operations/addLog", { id: opId, level: "success", message: name });
    }

    store.dispatch("operations/updateProgress", {
      id: opId,
      progress: { total: count, completed: count, failed: 0, pending: 0 },
    });
    store.dispatch("operations/finishOperation", {
      id: opId,
      status: "completed",
      summary: `Удалено: ${count}`,
    });
    http.post(`/operations/${opId}/finish`, { status: "completed", summary: `Удалено: ${count}` }, { cache: false }).catch(() => {});

    emit('filesChanged');
  } catch (error) {
    store.dispatch("operations/addLog", { id: opId, level: "error", message: error?.response?.data?.message || error?.message || "Ошибка" });
    store.dispatch("operations/finishOperation", {
      id: opId,
      status: "failed",
      summary: error?.response?.data?.message || "Ошибка удаления",
    });
    http.post(`/operations/${opId}/finish`, { status: "failed", summary: error?.message || "Ошибка" }, { cache: false }).catch(() => {});
    displaySonnerError(error);
  }
}

onMounted(() => {
  loadFolders();
  loadStoreTags();
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
  <div class="flex min-h-[inherit] flex-1 flex-col">
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

    <!-- Delete confirmation -->
    <AlertDialog v-model:open="showDeleteConfirm">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Удалить выбранные файлы?</AlertDialogTitle>
          <AlertDialogDescription>
            {{ selectedItems.filter(i => i.type === 'file').length }} файлов будет удалено. Это действие нельзя отменить.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Отмена</AlertDialogCancel>
          <AlertDialogAction @click="confirmDeleteSelected" :disabled="isDeletingSelected">
            Удалить
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <!-- File list -->
    <div
      class="relative min-h-[24rem] flex-1 overflow-auto"
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
        class="absolute inset-3 z-10 flex items-center justify-center rounded-lg border-2 border-dashed border-primary/40 bg-background/80 pointer-events-none"
      >
        <div class="flex flex-col items-center gap-1 text-muted-foreground">
          <Upload class="size-8" />
          <p class="text-sm font-medium">Перетащите файлы для загрузки</p>
        </div>
      </div>

      <div v-if="currentSubfolders.length === 0 && currentFiles.length === 0 && !isLoadingFolders && !inlineNewFolder && currentPendingUploads.length === 0 && currentFolderId == null"
        class="flex min-h-[24rem] flex-col items-center justify-center py-16 text-muted-foreground"
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
            <td></td>
            <td class="pr-3 w-10"></td>
          </tr>

          <!-- Inline new folder row -->
          <tr v-if="inlineNewFolder" class="border-b bg-muted/30 h-11">
            <td class="pl-4 pr-3 w-8">
              <Loader2 v-if="isCreatingFolder" class="size-4 text-muted-foreground animate-spin" />
              <Folder v-else class="size-4 text-muted-foreground" />
            </td>
            <td colspan="4">
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
              <span v-else class="block truncate max-w-[200px] sm:max-w-xs lg:max-w-md" :title="folder.name">{{ folder.name }}</span>
            </td>
            <td class="text-muted-foreground hidden sm:table-cell">Папка</td>
            <td></td>
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
              <span v-else class="block truncate max-w-[200px] sm:max-w-xs lg:max-w-md" :title="extractFileName(file)">{{ extractFileName(file) }}</span>
            </td>
            <td class="hidden sm:table-cell">
              <Badge variant="secondary" class="font-normal">{{ file.contentType }}</Badge>
            </td>
            <td class="px-4" @click.stop>
              <Popover>
                <PopoverTrigger as-child>
                  <button type="button" class="flex items-center gap-1 py-0.5 px-1 rounded hover:bg-muted/50 transition-colors">
                    <template v-if="file.tags?.length > 0">
                      <Badge
                        v-for="t in file.tags"
                        :key="t.id"
                        :class="[tagBgClass(t.color), tagTextClass(t.color), 'text-xs border-0']"
                      >{{ t.name }}</Badge>
                    </template>
                    <Tag v-else class="size-4 text-muted-foreground/30 opacity-0 group-hover:opacity-100 transition-opacity" />
                  </button>
                </PopoverTrigger>
                <PopoverContent align="start" class="w-64 p-0">
                  <!-- Existing tags list -->
                  <div class="p-1 max-h-48 overflow-y-auto">
                    <div v-for="tag in storeTags" :key="tag.id">
                      <div class="flex items-center gap-2 w-full px-2 py-1.5 rounded-sm text-sm hover:bg-accent transition-colors">
                        <!-- Color dot — click to edit color -->
                        <button
                          type="button"
                          :class="['size-5 rounded-full shrink-0 cursor-pointer hover:ring-2 hover:ring-primary/30 transition-shadow', tagBgClass(tag.color)]"
                          :title="'Сменить цвет'"
                          @click.stop="editingTagId = editingTagId === tag.id ? null : tag.id"
                        />
                        <!-- Tag name — click to toggle, dblclick to rename -->
                        <template v-if="renamingTagId === tag.id">
                          <input
                            v-model="renamingTagName"
                            maxlength="50"
                            class="flex-1 text-sm bg-transparent outline-none border rounded px-1 py-0.5"
                            @keydown.enter="commitRenameTag"
                            @keydown.escape="renamingTagId = null"
                            @blur="commitRenameTag"
                            @click.stop
                          />
                        </template>
                        <button v-else type="button" class="truncate flex-1 text-left" @click="toggleFileTag(file.id, tag.id)" @dblclick.stop="startRenameTag(tag)">
                          {{ tag.name }}
                        </button>
                        <Check v-if="fileHasTag(file.id, tag.id)" class="size-4 text-primary shrink-0" />
                      </div>
                      <!-- Color picker row for this tag -->
                      <div v-if="editingTagId === tag.id" class="flex gap-1.5 px-2 py-1.5 bg-muted/30 rounded-sm mx-1 mb-1">
                        <button
                          v-for="c in TAG_COLORS"
                          :key="c.value"
                          type="button"
                          :class="['size-5 rounded-full shrink-0 cursor-pointer transition-shadow', c.bg, tag.color === c.value ? 'ring-2 ring-primary ring-offset-1' : 'hover:ring-2 hover:ring-muted-foreground/30']"
                          :title="c.label"
                          @click="updateTagColor(tag.id, c.value)"
                        />
                      </div>
                    </div>
                  </div>
                  <!-- Create new tag -->
                  <div class="border-t p-2 space-y-2">
                    <div class="flex gap-1.5">
                      <button
                        v-for="c in TAG_COLORS"
                        :key="c.value"
                        type="button"
                        :class="['size-5 rounded-full shrink-0 cursor-pointer transition-shadow', c.bg, newTagColor === c.value ? 'ring-2 ring-primary ring-offset-1' : 'hover:ring-2 hover:ring-muted-foreground/30']"
                        :title="c.label"
                        @click="newTagColor = c.value"
                      />
                    </div>
                    <div class="flex items-center gap-1.5">
                      <Input
                        v-model="newTagName"
                        placeholder="Новый тэг"
                        maxlength="50"
                        class="h-8 text-sm"
                        @keydown.enter="createAndAssignTag(file.id)"
                      />
                      <Button size="sm" @click="createAndAssignTag(file.id)">
                        <Plus class="size-4" />
                      </Button>
                    </div>
                  </div>
                </PopoverContent>
              </Popover>
            </td>
            <td class="text-muted-foreground whitespace-nowrap">{{ formatBytes(file.length) }}</td>
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
            <td></td>
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
              @click.stop="deleteSingleFile(contextMenu.item.id); closeContextMenu()"
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
            :model-value="deleteFolderRecursive"
            @update:model-value="deleteFolderRecursive = $event"
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
    <div class="sticky bottom-0 flex shrink-0 items-center gap-2 border-t bg-background p-3 rounded-b-xl">
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
      <template v-if="selection.size > 0">
        <span class="ml-auto text-sm text-muted-foreground tabular-nums">{{ selection.size }} выбрано</span>
        <Button variant="outline" size="sm" @click="clearSelection">
          <X class="size-4 mr-1" />
          Снять
        </Button>
        <Popover>
          <PopoverTrigger as-child>
            <Button variant="outline" size="sm">
              <Tag class="size-4 mr-1" />
              Тэг
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" class="w-56 p-0">
            <div class="p-1 max-h-48 overflow-y-auto">
              <button
                v-for="tag in storeTags"
                :key="tag.id"
                type="button"
                class="flex items-center gap-2 w-full px-2 py-1.5 rounded-sm text-sm hover:bg-accent transition-colors"
                @click="bulkToggleTag(tag.id)"
              >
                <span :class="['size-5 rounded-full shrink-0', tagBgClass(tag.color)]" />
                <span class="truncate flex-1 text-left">{{ tag.name }}</span>
                <Check v-if="bulkAllHaveTag(tag.id)" class="size-4 text-primary shrink-0" />
              </button>
              <div v-if="storeTags.length === 0" class="text-sm text-muted-foreground text-center py-4">Нет тэгов</div>
            </div>
          </PopoverContent>
        </Popover>
        <Button variant="outline" size="sm" @click="deleteSelected">
          <Trash2 class="size-4 mr-1" />
          Удалить
        </Button>
      </template>
    </div>
  </div>
</template>
