<script setup>
import { Spinner } from "@/components/ui/spinner";
import { ref, reactive, computed, onMounted, onUnmounted, nextTick, watch } from "vue";
import { useStore } from "vuex";
import { useRoute, useRouter } from "vue-router";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { useFileDialog, useDebounceFn } from "@vueuse/core";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import Input from "@/components/ui/input/Input.vue";
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
  Folder,
  FolderOpen,
  FileIcon,
  Plus,
  Upload,
  Pencil,
  Trash2,
  ArrowDownToLine,
  ChevronRight,
  Share2,
  Check,
  X,
  CornerLeftUp,
  Globe,
} from "lucide-vue-next";
import ShareLinkDialog from "@/components/products/ShareLinkDialog.vue";
import { http } from "@/main";
import { FIELD_LIMITS, isNonEmptyWithinLimit } from "@/lib/fieldLimits";
import {
  MENU_ITEM,
  MENU_LABEL,
  MENU_SEPARATOR,
  MENU_SURFACE,
} from "@/lib/menuSurface";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import {
  Table,
  TableBody,
  TableCell,
  TableRow,
} from "@/components/ui/table";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import {
  Tag,
} from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import { MenuKbd } from "@/components/ui/kbd";
import { t } from "@/i18n";
import { randomUuidCompact } from "@/utils/uuid";
import { useStoreDateTime } from "@/composables/useStoreDateTime";

const props = defineProps({
  files: { type: Array, default: () => [] },
  productId: { type: Number, default: null },
  productName: { type: String, default: "" },
  productSku: { type: String, default: "" },
  scope: { type: String, default: "product" },
  storageLabel: { type: String, default: "" },
});

const emit = defineEmits(["deleteFile", "filesChanged"]);

const { formatDateTime: formatStoreDateTime } = useStoreDateTime();

// Soft refresh for parent (product images, etc.) — list itself updates immediately from upload responses.
const debouncedFilesChanged = useDebounceFn(() => {
  emit("filesChanged");
  loadStorageQuota();
  loadFolders();
}, 800);

const store = useStore();
const route = useRoute();
const router = useRouter();

const isStoreScope = computed(() => props.scope === "store");
const rootLabel = computed(() => isStoreScope.value ? t("products.productFileManager.storageRootLabel") : t("products.productFileManager.filesRootLabel"));
const subjectLabel = computed(() => {
  if (props.storageLabel) return props.storageLabel;
  if (isStoreScope.value) return t("products.productFileManager.storeSharedStorageLabel");
  return props.productSku
    ? `${props.productName || t("products.productFileManager.productFallback")} [${props.productSku}]`
    : (props.productName || t("products.productFileManager.productWithIdFallback", { id: props.productId }));
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

function filePublicToggleUrl(fileId) {
  return isStoreScope.value
    ? `/store-storage/files/${fileId}/public`
    : `/products/${productRequired()}/files/${fileId}/public`;
}

function listFilesUrl(folderId, startIndex, pageSize) {
  const params = new URLSearchParams();
  params.set("startIndex", String(startIndex));
  params.set("pageSize", String(pageSize));
  // inFolder scopes the list to a folder; omit folderId for root (null)
  params.set("inFolder", "1");
  if (folderId != null) params.set("folderId", String(folderId));
  if (isStoreScope.value) {
    return `/store-storage/files?${params}`;
  }
  return `/products/${productRequired()}/files?${params}`;
}

function normalizeFileDto(raw) {
  if (!raw || typeof raw !== "object") return raw;
  const id = raw.id ?? raw.Id;
  return { ...raw, id };
}

function unwrapUploadedFiles(data) {
  const body = data?.value ?? data;
  if (Array.isArray(body)) return body.map(normalizeFileDto);
  if (Array.isArray(body?.items)) return body.items.map(normalizeFileDto);
  return [];
}


function shareFolderUrl(folderId) {
  const id = folderId ?? "root";
  return isStoreScope.value
    ? `/store-storage/folders/${id}/share`
    : `/products/${productRequired()}/folders/${id}/share`;
}

function archiveFolderUrl(folderId) {
  const id = folderId ?? "root";
  return isStoreScope.value
    ? `/store-storage/folders/${id}/archive`
    : `/products/${productRequired()}/folders/${id}/archive`;
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

// File list owned by this component (paginated per folder; uploads merge immediately)
const localFiles = ref([]);
const filesTotalCount = ref(0);
const filesTotalSizeBytes = ref(0);
const isLoadingFiles = ref(false);
const isLoadingMoreFiles = ref(false);
const hasMoreFiles = ref(false);
const loadMoreSentinel = ref(null);
const fileListScrollEl = ref(null);
const canScrollY = ref(false);
const FILES_PAGE_SIZE = 50;
let nextFilesStartIndex = 0;
let loadMoreObserver = null;
let fileListResizeObserver = null;
// Values must match C# FileTagColor enum NAMES (JsonStringEnumConverter)
const TAG_COLORS = [
  { value: "Red", get label() { return t("products.productFileManager.tagColorRed"); }, bg: "bg-red-500", text: "text-white" },
  { value: "Blue", get label() { return t("products.productFileManager.tagColorBlue"); }, bg: "bg-blue-500", text: "text-white" },
  { value: "Green", get label() { return t("products.productFileManager.tagColorGreen"); }, bg: "bg-emerald-500", text: "text-white" },
  { value: "Yellow", get label() { return t("products.productFileManager.tagColorYellow"); }, bg: "bg-yellow-300", text: "text-yellow-900" },
  { value: "Orange", get label() { return t("products.productFileManager.tagColorOrange"); }, bg: "bg-orange-500", text: "text-white" },
  { value: "Purple", get label() { return t("products.productFileManager.tagColorPurple"); }, bg: "bg-purple-500", text: "text-white" },
  { value: "Balck", get label() { return t("products.productFileManager.tagColorBlack"); }, bg: "bg-neutral-800 dark:bg-neutral-300", text: "text-white dark:text-neutral-800" },
  { value: "White", get label() { return t("products.productFileManager.tagColorGray"); }, bg: "bg-neutral-200 dark:bg-neutral-600", text: "text-neutral-700 dark:text-neutral-100" },
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
    for (const file of localFiles.value) {
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
    for (const file of localFiles.value) {
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
  const file = localFiles.value.find(f => f.id === fileId);
  return file?.tags?.some(t => t.id === tagId) ?? false;
}

async function toggleFileTag(fileId, tagId) {
  const file = localFiles.value.find(f => f.id === fileId);
  const tag = storeTags.value.find(t => t.id === tagId);
  const hadTag = fileHasTag(fileId, tagId);

  // Optimistic UI - update instantly
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
  if (!isNonEmptyWithinLimit(newTagName.value, FIELD_LIMITS.tagName)) return;
  try {
    const res = await http.post("/tags", { name: newTagName.value.trim(), color: newTagColor.value }, { cache: false });
    const newTag = { id: res.data.Id ?? res.data.id, name: res.data.name, color: res.data.color };
    storeTags.value.push(newTag);
    await http.post(fileTagUrl(fileId, newTag.id), null, { cache: false });
    // Optimistic: add tag to file locally
    const file = localFiles.value.find(f => f.id === fileId);
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

  // Optimistic UI - update instantly
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
      displaySonnerSuccess(t("products.productFileManager.tagRemovedFromFiles", { count: files.length }));
    } else {
      const fileIds = files.filter(f => !snapshot.get(f.id)?.some(t => t.id === tagId)).map(f => f.id);
      await http.post(bulkTagUrl(), { fileIds, tagId }, { cache: false });
      displaySonnerSuccess(t("products.productFileManager.tagAssignedToFiles", { count: fileIds.length }));
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
const inlineNewFolderName = ref(t("products.productFileManager.newFolderDefaultName"));
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
const publicUrlCopied = ref(false);
const isCreatingShare = ref(false);
const shareCopied = ref(false);
const shareItemName = computed(() => {
  if (!shareTarget.value) return "";
  if (shareTarget.value.type === "file") {
    return extractFileName(shareTarget.value.item);
  }
  return shareTarget.value.item?.name ?? t("products.productFileManager.allFilesFallback");
});

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
        displaySonnerError(t("products.productFileManager.folderNotFound", { name: parts[i] }));
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

const currentFiles = computed(() => localFiles.value);

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

function mergeUploadedFiles(data) {
  const uploaded = unwrapUploadedFiles(data);
  const folder = currentFolderId.value ?? null;
  for (const file of uploaded) {
    const id = file.id;
    if (id == null) continue;
    if (localFiles.value.some((f) => f.id === id)) continue;
    if ((file.folderId ?? null) !== folder) continue;
    localFiles.value.unshift(file);
    filesTotalCount.value += 1;
    filesTotalSizeBytes.value += file.length || 0;
  }
}

async function loadFiles({ reset = false } = {}) {
  if (reset) {
    nextFilesStartIndex = 0;
    hasMoreFiles.value = true;
    localFiles.value = [];
    filesTotalCount.value = 0;
    filesTotalSizeBytes.value = 0;
  }
  if (!hasMoreFiles.value && !reset) return;
  if (isLoadingFiles.value || isLoadingMoreFiles.value) return;

  const isFirstPage = reset || nextFilesStartIndex === 0;
  if (isFirstPage) isLoadingFiles.value = true;
  else isLoadingMoreFiles.value = true;

  try {
    const response = await http.get(
      listFilesUrl(currentFolderId.value, nextFilesStartIndex, FILES_PAGE_SIZE),
      { cache: false },
    );
    const body = response.data?.value ?? response.data;
    const items = (Array.isArray(body) ? body : (body?.items || [])).map(normalizeFileDto);
    const lastPage = Array.isArray(body) ? true : !!body?.lastPage;
    const total = Array.isArray(body) ? items.length : (body?.totalCount ?? items.length);

    if (isFirstPage) {
      localFiles.value = items;
    } else {
      const seen = new Set(localFiles.value.map((f) => f.id));
      localFiles.value.push(...items.filter((f) => !seen.has(f.id)));
    }
    nextFilesStartIndex += items.length;
    hasMoreFiles.value = !lastPage && items.length > 0;
    filesTotalCount.value = total;
    if (isFirstPage) {
      filesTotalSizeBytes.value = localFiles.value.reduce((sum, f) => sum + (f.length || 0), 0);
    }
  } catch (error) {
    displaySonnerError(error);
    if (isFirstPage) {
      localFiles.value = [];
      hasMoreFiles.value = false;
    }
  } finally {
    isLoadingFiles.value = false;
    isLoadingMoreFiles.value = false;
  }
}

function setupLoadMoreObserver() {
  if (loadMoreObserver) {
    loadMoreObserver.disconnect();
    loadMoreObserver = null;
  }
  if (!loadMoreSentinel.value) return;
  const root = loadMoreSentinel.value.closest(".overflow-auto") || null;
  loadMoreObserver = new IntersectionObserver(
    (entries) => {
      if (entries.some((e) => e.isIntersecting)) {
        loadFiles({ reset: false });
      }
    },
    { root, rootMargin: "120px", threshold: 0 },
  );
  loadMoreObserver.observe(loadMoreSentinel.value);
}

function checkFileListScroll() {
  const el = fileListScrollEl.value;
  if (!el) {
    canScrollY.value = false;
    return;
  }
  canScrollY.value = el.scrollHeight > el.clientHeight + 8;
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
    ghost.textContent = t("products.productFileManager.dragItemsCount", { count });
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
    return;
  }

  // Drop always leaves the current folder view — sync local list immediately
  // (filesChanged only soft-refreshes the parent; it does not reload this list).
  if (fileIds.length > 0) {
    const idSet = new Set(fileIds);
    const removed = localFiles.value.filter((f) => idSet.has(f.id));
    localFiles.value = localFiles.value.filter((f) => !idSet.has(f.id));
    filesTotalCount.value = Math.max(0, filesTotalCount.value - removed.length);
    filesTotalSizeBytes.value = Math.max(
      0,
      filesTotalSizeBytes.value - removed.reduce((sum, f) => sum + (f.length || 0), 0),
    );
    emit("filesChanged");
  }

  if (folderIds.length > 0) {
    const idSet = new Set(folderIds);
    for (const folder of folders.value) {
      if (idSet.has(folder.id)) {
        folder.parentFolderId = targetFolderId;
      }
    }
  }

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

function formatQuotaBytes(bytes) {
  const n = Number(bytes) || 0;
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(n < 10 * 1024 ? 1 : 0)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / (1024 * 1024)).toFixed(n < 10 * 1024 * 1024 ? 1 : 0)} MB`;
  return `${(n / (1024 * 1024 * 1024)).toFixed(n < 10 * 1024 * 1024 * 1024 ? 1 : 0)} GB`;
}

/** Compact label for the path-bar hint, e.g. "2 GB / 4 GB". */
function formatCompactQuotaBytes(bytes) {
  const n = Number(bytes) || 0;
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${Math.round(n / 1024)} KB`;
  if (n < 1024 * 1024 * 1024) {
    const mb = n / (1024 * 1024);
    return mb < 10 ? `${mb.toFixed(1)} MB` : `${Math.round(mb)} MB`;
  }
  const gb = n / (1024 * 1024 * 1024);
  return gb < 10 ? `${gb.toFixed(1)} GB` : `${Math.round(gb)} GB`;
}

const storageQuota = ref(null); // { used, limit } bytes

const storageQuotaLabel = computed(() => {
  const q = storageQuota.value;
  if (!q || q.used == null) return null;
  const used = formatCompactQuotaBytes(q.used);
  if (q.limit == null) return used;
  return `${used} / ${formatCompactQuotaBytes(q.limit)}`;
});

function applyStorageQuota(quota) {
  const storage = quota?.storageBytes;
  if (!storage || storage.used == null) return;
  storageQuota.value = {
    used: Number(storage.used) || 0,
    limit: storage.limit != null ? Number(storage.limit) : null,
  };
}

async function loadStorageQuota() {
  try {
    const res = await http.get("/store-storage/quota", { cache: false });
    applyStorageQuota(res.data?.value ?? res.data);
  } catch {
    // Non-critical chrome hint
  }
}

/** Returns false when the batch cannot fit — shows one clear toast and skips upload. */
async function assertUploadFitsQuota(filesToUpload) {
  const neededBytes = filesToUpload.reduce((sum, f) => sum + (f.size || 0), 0);
  const neededFiles = filesToUpload.length;
  try {
    const res = await http.get("/store-storage/quota", { cache: false });
    const quota = res.data?.value ?? res.data;
    applyStorageQuota(quota);
    const files = quota?.files;
    const storage = quota?.storageBytes;

    if (files?.limit != null && files.used + neededFiles > files.limit) {
      displaySonnerError(t("errors.quota_files_reached", {
        used: files.used,
        limit: files.limit,
        remaining: Math.max(0, files.remaining ?? (files.limit - files.used)),
        needed: neededFiles,
      }));
      return false;
    }

    if (storage?.limit != null && storage.used + neededBytes > storage.limit) {
      const remaining = Math.max(0, storage.remaining ?? (storage.limit - storage.used));
      displaySonnerError(t("errors.quota_storage_reached", {
        needed: formatQuotaBytes(neededBytes),
        remaining: formatQuotaBytes(remaining),
        limit: formatQuotaBytes(storage.limit),
      }));
      return false;
    }
  } catch {
    // Fail open — server still enforces quotas on upload.
  }
  return true;
}

async function uploadFiles(fileList) {
  const filesToUpload = Array.from(fileList);
  if (filesToUpload.length === 0) return;

  if (!(await assertUploadFitsQuota(filesToUpload))) return;

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

  // Master abort controller - when triggered, cancels ALL in-flight uploads for this batch.
  const batchAbort = new AbortController();
  const totalFiles = entries.length;

  // Register operation in Vuex FIRST (before backend call) so the local version
  // with silent+onCancel wins the race against SignalR OperationStarted event.
  // ADD_OPERATION deduplicates by ID - whoever adds first keeps the entry.
  const opId = randomUuidCompact();
  const uploadTitle = totalFiles === 1
    ? t("products.productFileManager.uploadingFileTitle", { subject: subjectLabel.value })
    : t("products.productFileManager.uploadingFilesTitle", { count: totalFiles, subject: subjectLabel.value });

  store.dispatch("operations/addOperation", {
    id: opId,
    type: "file-upload",
    title: uploadTitle,
    progress: { total: totalFiles, completed: 0, failed: 0, pending: totalFiles },
    silent: true,
    batchType: "client",
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

  // Then persist to backend (fire-and-forget - Vuex state is already set).
  // This creates the Operation DB record with author info for history.
  http.post("/operations/start", {
    operationId: opId,
    type: "file-upload",
    title: uploadTitle,
    batchType: "client",
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
      const response = await http({
        url,
        method: 'PUT',
        data: formData,
        timeout: 15 * 60 * 1000,
        cache: false,
        signal: entry.abortController.signal,
        onUploadProgress: (e) => {
          if (e.total) entry.percent = Math.round((e.loaded * 100) / e.total);
        },
      });
      entry.status = 'done';
      completedFiles++;
      anyDone = true;
      // Show the uploaded file immediately from the API response (before batch ends).
      mergeUploadedFiles(response.data);
      pendingUploads.value = pendingUploads.value.filter(u => u.id !== entry.id);
      updateOpProgress();
      store.dispatch("operations/addLog", { id: opId, level: "success", message: entry.name });
      // Soft refresh parent (images tab / quotas); list itself already updated.
      debouncedFilesChanged();
    } catch (error) {
      if (error?.code === 'ERR_CANCELED' || entry.abortController.signal.aborted || batchAbort.signal.aborted) {
        entry.status = 'cancelled';
        cancelledFiles++;
        updateOpProgress();
        store.dispatch("operations/addLog", { id: opId, level: "warning", message: t("products.productFileManager.logCancelledEntry", { name: entry.name }) });
      } else {
        entry.status = 'error';
        failedFiles++;
        updateOpProgress();
        store.dispatch("operations/addLog", { id: opId, level: "error", message: `${entry.name}: ${error?.response?.data?.title || error?.response?.data?.message || error?.message || t("products.productFileManager.errorFallback")}` });
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

  // Finish operation - but only if it wasn't already cancelled by stopOperation.
  // stopOperation calls FINISH_OPERATION(cancelled) before the loop ends;
  // we must not overwrite it.
  if (!batchAbort.signal.aborted) {
    const finalStatus = failedFiles > 0 ? (completedFiles > 0 ? "partial" : "failed") : "completed";
    const finalSummary = t("products.productFileManager.uploadedSummary", { count: completedFiles }) + (failedFiles > 0 ? t("products.productFileManager.uploadedSummaryErrorsSuffix", { count: failedFiles }) : "");
    store.dispatch("operations/finishOperation", {
      id: opId,
      status: finalStatus,
      summary: finalSummary,
    });
    try {
      await http.post(`/operations/${opId}/finish`, { status: finalStatus, summary: finalSummary }, { cache: false });
    } catch { /* ignore */ }
  } else {
    // Batch was cancelled - persist cancelled status to backend
    const summary = t("products.productFileManager.uploadCancelledSummary", { completed: completedFiles, cancelled: cancelledFiles });
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
    loadStorageQuota();
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
  inlineNewFolderName.value = t("products.productFileManager.newFolderDefaultName");
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
  if (!isNonEmptyWithinLimit(name, FIELD_LIMITS.folderName)) {
    displaySonnerError(t("common.tooLong", { max: FIELD_LIMITS.folderName }));
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
  if (!isNonEmptyWithinLimit(newName, FIELD_LIMITS.folderName)) {
    displaySonnerError(t("common.tooLong", { max: FIELD_LIMITS.folderName }));
    return;
  }

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
      const file = localFiles.value.find((f) => f.id === target.id);
      if (file) file.fileName = newName;
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
    const file = localFiles.value.find(f => f.id === id);
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
    const response = await http.delete(
      `${foldersUrl(deleteFolderTarget.value.id)}?recursive=${!!(!isEmpty && deleteFolderRecursive.value)}`,
      { cache: false },
    );
    const data = response.data?.value ?? response.data;
    const removeIds = [deleteFolderTarget.value.id];
    if (deleteFolderRecursive.value) {
      collectDescendantIds(deleteFolderTarget.value.id, removeIds);
    }
    folders.value = folders.value.filter((f) => !removeIds.includes(f.id));
    clearSelection();
    if (data?.queued) {
      displaySonnerSuccess(t("products.productFileManager.folderDeleteQueued"));
    } else {
      displaySonnerSuccess(t("products.productFileManager.folderDeleted"));
    }
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
  shareCopied.value = false;
  publicUrlCopied.value = false;
  if (type === 'file' && item.isPublic && item.publicUrl) {
    shareTtlHours.value = "permanent";
    shareUrl.value = `${window.location.origin}${item.publicUrl}`;
  } else {
    shareTtlHours.value = "168";
    shareUrl.value = "";
  }
  showShareDialog.value = true;
}

async function createShareLink() {
  if (!shareTarget.value || isCreatingShare.value) return;
  isCreatingShare.value = true;

  if (shareTtlHours.value === 'permanent') {
    try {
      const res = await http.patch(filePublicToggleUrl(shareTarget.value.item.id), { isPublic: true }, { cache: false });
      const updated = res.data?.value ?? res.data;
      shareTarget.value.item.isPublic = updated.isPublic;
      shareTarget.value.item.publicUrl = updated.publicUrl ?? null;
      shareUrl.value = `${window.location.origin}${updated.publicUrl}`;
    } catch (error) {
      displaySonnerError(error);
    }
    isCreatingShare.value = false;
    return;
  }

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
    displaySonnerSuccess(t("products.productFileManager.linkCopied"));
    setTimeout(() => { shareCopied.value = false; }, 2000);
  } catch {
    displaySonnerError(t("products.productFileManager.copyFailed"));
  }
}

async function copyPublicUrl(file) {
  try {
    await navigator.clipboard.writeText(`${window.location.origin}${file.publicUrl}`);
    publicUrlCopied.value = true;
    displaySonnerSuccess(t("products.productFileManager.linkCopied"));
    setTimeout(() => { publicUrlCopied.value = false; }, 2000);
  } catch {
    displaySonnerError(t("products.productFileManager.copyFailed"));
  }
}

async function revokePublicAccess() {
  if (!shareTarget.value || shareTarget.value.type !== "file" || isCreatingShare.value) return;
  isCreatingShare.value = true;
  try {
    const res = await http.patch(
      filePublicToggleUrl(shareTarget.value.item.id),
      { isPublic: false },
      { cache: false },
    );
    const updated = res.data?.value ?? res.data;
    shareTarget.value.item.isPublic = updated.isPublic === true;
    shareTarget.value.item.publicUrl = updated.publicUrl ?? null;
    shareUrl.value = "";
    shareTtlHours.value = "168";
    displaySonnerSuccess(t("products.productFileManager.publicAccessStopped"));
  } catch (error) {
    displaySonnerError(error);
  }
  isCreatingShare.value = false;
}


const isDownloadingArchive = ref(false);
const downloadingFolderId = ref(null);
const MAX_ARCHIVE_BYTES = 10 * 1024 * 1024 * 1024;
const MAX_ARCHIVE_FILES = 50000;

function getFolderArchiveStats(folderId) {
  if (folderId == null) {
    return {
      totalFileCount: filesTotalCount.value || localFiles.value.length,
      totalSizeBytes: filesTotalSizeBytes.value || localFiles.value.reduce((sum, f) => sum + (f.length || 0), 0),
    };
  }
  const folder = folders.value.find((f) => f.id === folderId);
  return {
    totalFileCount: folder?.totalFileCount ?? folder?.fileCount ?? 0,
    totalSizeBytes: folder?.totalSizeBytes ?? 0,
  };
}

async function downloadFolderArchive(folderId) {
  if (isDownloadingArchive.value) return;
  const stats = getFolderArchiveStats(folderId);
  if (stats.totalFileCount > MAX_ARCHIVE_FILES || stats.totalSizeBytes > MAX_ARCHIVE_BYTES) {
    displaySonnerError(t("products.productFileManager.archiveTooLarge", {
      files: stats.totalFileCount,
      size: formatBytes(stats.totalSizeBytes),
    }));
    return;
  }

  isDownloadingArchive.value = true;
  downloadingFolderId.value = folderId;
  try {
    await http.post(archiveFolderUrl(folderId), null, { cache: false });
    displaySonnerSuccess(t("products.productFileManager.archiveQueued"));
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
const pendingDeleteFileId = ref(null);

async function deleteSelected() {
  const items = selectedItems.value;
  if (items.length === 0) return;
  const folders = items.filter((i) => i.type === "folder");
  const files = items.filter((i) => i.type === "file");
  if (files.length === 0 && folders.length === 1) {
    openDeleteFolderDialog(folders[0].item);
    return;
  }
  if (files.length === 0) return;
  showDeleteConfirm.value = true;
}

async function confirmDeleteSelected() {
  if (pendingDeleteFileId.value) {
    await confirmDeletePendingFile();
    return;
  }

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
function deleteSingleFile(fileId) {
  pendingDeleteFileId.value = fileId;
  showDeleteConfirm.value = true;
}

async function confirmDeletePendingFile() {
  const fileId = pendingDeleteFileId.value;
  if (!fileId) return;

  isDeletingSelected.value = true;
  const file = localFiles.value.find((f) => f.id === fileId);
  const name = file?.fileName || file?.displayName || file?.name || `#${fileId}`;
  await deleteFilesWithOperation([fileId], [name]);
  pendingDeleteFileId.value = null;
  isDeletingSelected.value = false;
  showDeleteConfirm.value = false;
}

// Unified delete: registers operation, calls bulk-delete, logs results
async function deleteFilesWithOperation(fileIds, fileNames) {
  const count = fileIds.length;
  const opId = randomUuidCompact();
  const title = count === 1
    ? t("products.productFileManager.deletingFileTitle", { subject: subjectLabel.value })
    : t("products.productFileManager.deletingFilesTitle", { count, subject: subjectLabel.value });

  // Register in Vuex first (wins SignalR race), then backend
  store.dispatch("operations/addOperation", {
    id: opId,
    type: "file-delete",
    title,
    progress: { total: count, completed: 0, failed: 0, pending: count },
    silent: true,
    batchType: "client",
  });
  http.post("/operations/start", {
    operationId: opId,
    type: "file-delete",
    title,
    batchType: "client",
    totalItems: count,
  }, { cache: false }).catch(() => {});

  store.dispatch("operations/addLog", { id: opId, level: "info", message: subjectLabel.value });

  try {
    await http.post(bulkDeleteFilesUrl(), { fileIds }, { cache: false });

    const idSet = new Set(fileIds);
    localFiles.value = localFiles.value.filter((f) => !idSet.has(f.id));
    filesTotalCount.value = Math.max(0, filesTotalCount.value - fileIds.length);

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
      summary: t("products.productFileManager.deletedSummary", { count }),
    });
    http.post(`/operations/${opId}/finish`, { status: "completed", summary: t("products.productFileManager.deletedSummary", { count }) }, { cache: false }).catch(() => {});

    emit('filesChanged');
  } catch (error) {
    store.dispatch("operations/addLog", { id: opId, level: "error", message: error?.response?.data?.message || error?.message || t("products.productFileManager.errorFallback") });
    store.dispatch("operations/finishOperation", {
      id: opId,
      status: "failed",
      summary: error?.response?.data?.message || t("products.productFileManager.deleteErrorFallback"),
    });
    http.post(`/operations/${opId}/finish`, { status: "failed", summary: error?.message || t("products.productFileManager.errorFallback") }, { cache: false }).catch(() => {});
    displaySonnerError(error);
  }
}

onMounted(async () => {
  loadFolders();
  loadStoreTags();
  loadStorageQuota();
  document.addEventListener('keydown', onKeyDown);
  await loadFiles({ reset: true });
  await nextTick();
  setupLoadMoreObserver();
  checkFileListScroll();
});

onUnmounted(() => {
  document.removeEventListener('keydown', onKeyDown);
  if (loadMoreObserver) {
    loadMoreObserver.disconnect();
    loadMoreObserver = null;
  }
  fileListScrollEl.value?.removeEventListener("scroll", checkFileListScroll);
  fileListResizeObserver?.disconnect();
});

watch(currentFolderId, () => {
  clearSelection();
  loadFiles({ reset: true }).then(() => nextTick(() => setupLoadMoreObserver()));
});

watch(loadMoreSentinel, () => {
  nextTick(() => setupLoadMoreObserver());
});

watch(fileListScrollEl, (el, oldEl) => {
  if (oldEl) oldEl.removeEventListener("scroll", checkFileListScroll);
  fileListResizeObserver?.disconnect();
  if (el) {
    el.addEventListener("scroll", checkFileListScroll);
    fileListResizeObserver = new ResizeObserver(checkFileListScroll);
    fileListResizeObserver.observe(el);
    checkFileListScroll();
  } else {
    canScrollY.value = false;
  }
}, { flush: "post" });

watch(
  [
    () => localFiles.value.length,
    () => folders.value.length,
    () => pendingUploads.value.length,
    hasMoreFiles,
    isLoadingMoreFiles,
    inlineNewFolder,
    currentFolderId,
  ],
  () => nextTick(checkFileListScroll),
);

watch(() => props.productId, () => {
  const wasRoot = currentFolderId.value == null;
  currentFolderId.value = null;
  folders.value = [];
  clearSelection();
  router.replace({ query: { ...route.query, folder: undefined } });
  loadFolders();
  if (wasRoot) loadFiles({ reset: true });
});

const selectedFileCount = computed(
  () => selectedItems.value.filter((i) => i.type === "file").length,
);
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
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
          <Button
            type="button"
            variant="link"
            class="h-auto p-0 hover:underline truncate font-normal"
            :class="[
              i === breadcrumbs.length - 1 ? 'font-medium' : 'text-muted-foreground',
              dragItems.length > 0 && dropTargetId === crumb.id ? 'bg-accent rounded px-1' : ''
            ]"
            @click.stop="navigateToFolder(crumb.id)"
            @dragover.prevent="dropTargetId = crumb.id"
            @dragleave="dropTargetId = null"
            @drop="onDrop($event, crumb.id)"
          >
            {{ crumb.name }}
          </Button>
        </template>
        <!-- Spacer to catch click for editing -->
        <span class="flex-1" @click="startPathEdit" />
      </template>
      <span
        v-if="storageQuotaLabel"
        class="shrink-0 tabular-nums text-[11px] text-muted-foreground/50 select-none"
        :title="t('products.productFileManager.storageQuotaHint')"
      >
        {{ storageQuotaLabel }}
      </span>
    </div>

    <!-- Delete confirmation -->
    <AlertDialog
      v-model:open="showDeleteConfirm"
      @update:open="(v) => { if (!v) pendingDeleteFileId = null; }"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {{ pendingDeleteFileId ? t("products.productFileManager.deleteFileTitle") : t("products.productFileManager.deleteSelectedFilesTitle") }}
          </AlertDialogTitle>
          <AlertDialogDescription>
            <template v-if="pendingDeleteFileId">
              {{ t("products.productFileManager.deleteFileIrreversible") }}
            </template>
            <template v-else-if="selectedItems.filter(i => i.type === 'file').length > 0">
              {{ t("products.productFileManager.deleteFilesCountIrreversible", { count: selectedItems.filter(i => i.type === 'file').length }) }}
            </template>
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            @click="confirmDeleteSelected"
            :disabled="isDeletingSelected"
          >
            {{ t("common.delete") }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <!-- File list -->
    <div
      ref="fileListScrollEl"
      class="relative flex min-h-0 flex-1 flex-col overflow-auto"
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
          <p class="text-sm font-medium">{{ t("products.productFileManager.dropFilesToUpload") }}</p>
        </div>
      </div>

      <EmptyState
        v-if="isLoadingFiles && currentFiles.length === 0 && currentPendingUploads.length === 0"
        loading
        :framed="false"
        class="min-h-[16rem]"
      />

      <EmptyState
        v-else-if="currentSubfolders.length === 0 && currentFiles.length === 0 && !isLoadingFolders && !isLoadingFiles && !inlineNewFolder && currentPendingUploads.length === 0 && currentFolderId == null"
        :framed="false"
        :title="t('products.productFileManager.emptyFolderTitle')"
        :description="t('products.productFileManager.emptyFolderDescription')"
      >
        <template #icon>
          <Folder />
        </template>
      </EmptyState>

      <Table
        v-if="!isLoadingFiles || currentFiles.length > 0 || currentFolderId != null || currentSubfolders.length > 0 || inlineNewFolder || currentPendingUploads.length > 0"
        :contain="false"
        class="w-full"
      >
        <colgroup>
          <col class="w-10" />
          <col class="w-full" />
          <col class="hidden w-48 sm:table-column" />
          <col />
          <col class="w-36" />
          <col class="w-20" />
          <col class="w-16" />
        </colgroup>
        <TableBody
          :class="canScrollY
            ? '[&_tr:last-child]:border-0'
            : '[&_tr:last-child]:!border-b'"
        >
          <!-- Go up row (..) -->
          <TableRow
            v-if="currentFolderId != null"
            class="border-b hover:bg-muted/50 cursor-pointer h-11 text-muted-foreground"
            @click="navigateToFolder(parentFolderId)"
            @dragover.prevent="dropTargetId = parentFolderId"
            @dragleave="dropTargetId = null"
            @drop="onDrop($event, parentFolderId)"
            :class="{ 'bg-accent ring-2 ring-primary/30': dragItems.length > 0 && dropTargetId === parentFolderId }"
          >
            <TableCell class="pl-4 pr-3 w-10">
              <CornerLeftUp class="size-4" />
            </TableCell>
            <TableCell class="min-w-0">..</TableCell>
            <TableCell />
            <TableCell />
            <TableCell />
            <TableCell />
            <TableCell class="pr-3 w-10"></TableCell>
          </TableRow>

          <!-- Inline new folder row -->
          <TableRow v-if="inlineNewFolder" class="border-b bg-muted/30 h-11">
            <TableCell class="pl-4 pr-3 w-10">
              <Spinner v-if="isCreatingFolder" class="text-muted-foreground" />
              <Folder v-else class="size-4 text-muted-foreground" />
            </TableCell>
            <TableCell class="min-w-0" colspan="5">
              <input
                ref="inlineNewFolderInput"
                v-model="inlineNewFolderName"
                :maxlength="FIELD_LIMITS.folderName"
                class="w-full bg-transparent outline-none border border-primary/40 rounded px-1.5 py-0.5 text-sm select-text"
                @keydown.enter="commitInlineCreate"
                @keydown.escape="cancelInlineCreate"
                @blur="commitInlineCreate"
                :disabled="isCreatingFolder"
              />
            </TableCell>
            <TableCell class="pr-3 w-10"></TableCell>
          </TableRow>

          <!-- Folders -->
          <TableRow
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
            <TableCell class="pl-4 pr-3 w-10">
              <Spinner v-if="isDownloadingArchive && downloadingFolderId === folder.id" class="text-muted-foreground" />
              <Folder v-else class="size-4 text-muted-foreground" />
            </TableCell>
            <TableCell class="min-w-0">
              <input
                v-if="inlineRenameTarget && inlineRenameTarget.type === 'folder' && inlineRenameTarget.id === folder.id"
                ref="inlineRenameInput"
                v-model="inlineRenameTarget.name"
                :maxlength="FIELD_LIMITS.folderName"
                class="w-full bg-transparent outline-none border border-primary/40 rounded px-1.5 py-0.5 text-sm select-text"
                @keydown.enter="commitInlineRename"
                @keydown.escape="cancelInlineRename"
                @blur="commitInlineRename"
                @click.stop
                @dblclick.stop
                @mousedown.stop
                @dragstart.stop.prevent
              />
              <span v-else class="block truncate" :title="folder.name">{{ folder.name }}</span>
            </TableCell>
            <TableCell class="hidden text-muted-foreground sm:table-cell">{{ t("products.productFileManager.folderTypeLabel") }}</TableCell>
            <TableCell />
            <TableCell />
            <TableCell class="text-muted-foreground">
              {{ folder.fileCount }} {{ folder.fileCount === 1 ? t("products.productFileManager.fileCountOne") : t("products.productFileManager.fileCountMany") }}
            </TableCell>
            <TableCell class="pr-3 w-10"></TableCell>
          </TableRow>

          <!-- Files -->
          <TableRow
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
            <TableCell class="pl-4 pr-3 w-10">
              <FileIcon class="size-4 text-muted-foreground" />
            </TableCell>
            <TableCell class="min-w-0">
              <input
                v-if="inlineRenameTarget && inlineRenameTarget.type === 'file' && inlineRenameTarget.id === file.id"
                ref="inlineRenameInput"
                v-model="inlineRenameTarget.name"
                :maxlength="FIELD_LIMITS.folderName"
                class="w-full bg-transparent outline-none border border-primary/40 rounded px-1.5 py-0.5 text-sm select-text"
                @keydown.enter="commitInlineRename"
                @keydown.escape="cancelInlineRename"
                @blur="commitInlineRename"
                @click.stop
                @dblclick.stop
                @mousedown.stop
                @dragstart.stop.prevent
              />
              <span v-else class="block truncate" :title="extractFileName(file)">{{ extractFileName(file) }}</span>
            </TableCell>
            <TableCell class="hidden sm:table-cell">
              <Badge
                variant="secondary"
                class="font-normal"
                :title="file.contentType"
              >{{ file.contentType }}</Badge>
            </TableCell>
            <TableCell class="px-4" @click.stop>
              <Popover>
                <PopoverTrigger as-child>
                  <Button
                    type="button"
                    variant="ghost"
                    :size="file.tags?.length ? undefined : 'icon'"
                    :class="
                      file.tags?.length
                        ? 'h-auto max-w-none flex flex-nowrap items-center gap-1 py-0.5 px-1 rounded hover:bg-muted/50'
                        : 'size-7 opacity-0 group-hover:opacity-100 group-focus-within:opacity-100'
                    "
                    :title="t('products.productFileManager.tagLabel')"
                    :aria-label="t('products.productFileManager.tagLabel')"
                  >
                    <template v-if="file.tags?.length">
                      <Badge
                        v-for="tagItem in file.tags"
                        :key="tagItem.id"
                        :class="[tagBgClass(tagItem.color), tagTextClass(tagItem.color), 'text-xs border-0']"
                      >{{ tagItem.name }}</Badge>
                    </template>
                    <Tag v-else class="size-3.5" />
                  </Button>
                </PopoverTrigger>
                <PopoverContent align="start" class="w-64 p-0">
                  <!-- Existing tags list -->
                  <div class="p-1 max-h-48 overflow-y-auto">
                    <div v-for="tag in storeTags" :key="tag.id">
                      <div :class="MENU_ITEM">
                        <!-- Color dot - click to edit color -->
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon"
                          :class="['size-5 min-h-0 rounded-full shrink-0 p-0 hover:ring-2 hover:ring-primary/30 transition-shadow', tagBgClass(tag.color)]"
                          :title="t('products.productFileManager.changeColorTitle')"
                          @click.stop="editingTagId = editingTagId === tag.id ? null : tag.id"
                        />
                        <!-- Tag name - click to toggle, dblclick to rename -->
                        <template v-if="renamingTagId === tag.id">
                          <input
                            v-model="renamingTagName"
                            :maxlength="FIELD_LIMITS.tagName"
                            class="flex-1 text-sm bg-transparent outline-none border rounded px-1 py-0.5"
                            @keydown.enter="commitRenameTag"
                            @keydown.escape="renamingTagId = null"
                            @blur="commitRenameTag"
                            @click.stop
                          />
                        </template>
                        <Button v-else type="button" variant="ghost" class="h-auto truncate flex-1 justify-start text-left font-normal p-0 hover:bg-transparent" @click="toggleFileTag(file.id, tag.id)" @dblclick.stop="startRenameTag(tag)">
                          {{ tag.name }}
                        </Button>
                        <Check v-if="fileHasTag(file.id, tag.id)" class="size-4 text-primary shrink-0" />
                      </div>
                      <!-- Color picker row for this tag -->
                      <div v-if="editingTagId === tag.id" class="flex gap-1.5 px-2 py-1.5 bg-muted/30 rounded-sm mx-1 mb-1">
                        <Button
                          v-for="c in TAG_COLORS"
                          :key="c.value"
                          type="button"
                          variant="ghost"
                          size="icon"
                          :class="['size-5 min-h-0 rounded-full shrink-0 p-0 transition-shadow', c.bg, tag.color === c.value ? 'ring-2 ring-primary ring-offset-1' : 'hover:ring-2 hover:ring-muted-foreground/30']"
                          :title="c.label"
                          @click="updateTagColor(tag.id, c.value)"
                        />
                      </div>
                    </div>
                    <div v-if="storeTags.length === 0" class="text-sm text-muted-foreground text-center py-4 px-2">
                      {{ t("products.productFileManager.noTagsLabel") }}
                    </div>
                  </div>
                  <!-- Create new tag -->
                  <div class="border-t p-2 space-y-2">
                    <div class="flex gap-1.5">
                      <Button
                        v-for="c in TAG_COLORS"
                        :key="c.value"
                        type="button"
                        variant="ghost"
                        size="icon"
                        :class="['size-5 min-h-0 rounded-full shrink-0 p-0 transition-shadow', c.bg, newTagColor === c.value ? 'ring-2 ring-primary ring-offset-1' : 'hover:ring-2 hover:ring-muted-foreground/30']"
                        :title="c.label"
                        @click="newTagColor = c.value"
                      />
                    </div>
                    <div class="flex items-center gap-1.5">
                      <Input
                        v-model="newTagName"
                        :placeholder="t('products.productFileManager.newTagPlaceholder')"
                        :maxlength="FIELD_LIMITS.tagName"
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
            </TableCell>
            <TableCell class="w-36 text-muted-foreground whitespace-nowrap">
              {{ file.uploadDateTime ? formatStoreDateTime(file.uploadDateTime) : "" }}
            </TableCell>
            <TableCell class="w-20 text-muted-foreground whitespace-nowrap">{{ formatBytes(file.length) }}</TableCell>
            <TableCell class="pr-3">
              <div class="flex items-center gap-0.5">
                <Button
                  v-if="file.isPublic && file.publicUrl"
                  variant="ghost"
                  size="icon"
                  class="size-7 opacity-0 group-hover:opacity-100 text-green-500"
                  :title="t('products.productFileManager.copyPublicLinkTitle')"
                  @click.stop="copyPublicUrl(file)"
                >
                  <Globe class="size-3.5" />
                </Button>
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
              </div>
            </TableCell>
          </TableRow>

          <!-- Uploading files -->
          <TableRow
            v-for="entry in currentPendingUploads"
            :key="'upload-' + entry.id"
            class="border-b h-11"
            :class="entry.status === 'done' ? 'bg-success/5' : 'animate-pulse bg-muted/40'"
          >
            <TableCell class="pl-4 pr-3 w-10">
              <Spinner v-if="entry.status === 'uploading'" class="text-muted-foreground" />
              <Check v-else-if="entry.status === 'done'" class="size-4 text-success" />
              <FileIcon v-else class="size-4 text-muted-foreground" />
            </TableCell>
            <TableCell class="relative min-w-0 text-muted-foreground">
              <span class="block truncate" :title="entry.name">{{ entry.name }}</span>
              <div
                v-if="entry.status !== 'done'"
                class="absolute bottom-0 left-0 right-0 h-0.5"
              >
                <div
                  class="h-full bg-primary/40 transition-all duration-300 ease-out"
                  :style="{ width: entry.percent + '%' }"
                />
              </div>
            </TableCell>
            <TableCell class="hidden text-xs tabular-nums text-muted-foreground sm:table-cell">
              {{ entry.percent }}%
            </TableCell>
            <TableCell />
            <TableCell />
            <TableCell class="text-muted-foreground">{{ formatBytes(entry.size) }}</TableCell>
            <TableCell class="pr-3">
              <Button
                v-if="entry.status !== 'done'"
                variant="ghost"
                size="icon"
                class="size-7"
                @click.stop="cancelUpload(entry.id)"
              >
                <X class="size-3.5" />
              </Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>

      <div
        v-if="hasMoreFiles || isLoadingMoreFiles"
        ref="loadMoreSentinel"
        class="flex justify-center py-3"
      >
        <Spinner v-if="isLoadingMoreFiles" size="sm" class="text-muted-foreground" />
      </div>
    </div>

    <!-- Context menu -->
    <Teleport to="body">
      <div v-if="contextMenu.show" class="fixed inset-0 z-50" @click="closeContextMenu" @contextmenu.prevent="closeContextMenu">
        <div
          ref="contextMenuEl"
          class="fixed z-[var(--z-popover)] w-56 min-w-[10rem] max-w-[14rem] p-1 animate-in fade-in-0 zoom-in-95"
          :class="MENU_SURFACE"
          :style="{ left: contextMenu.x + 'px', top: contextMenu.y + 'px' }"
        >
          <!-- Folder -->
          <template v-if="contextMenu.type === 'folder'">
            <div class="min-w-0 px-2 py-1.5">
              <p class="truncate text-sm font-medium text-foreground">
                {{ contextMenu.item?.name }}
              </p>
            </div>
            <div :class="MENU_SEPARATOR" />
            <p :class="MENU_LABEL">
              {{ t("products.productFileManager.menuSectionOpen") }}
            </p>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="navigateToFolder(contextMenu.item.id); closeContextMenu()"
            >
              <FolderOpen class="size-4 text-muted-foreground" />
              {{ t("products.productFileManager.openAction") }}
            </Button>
            <div :class="MENU_SEPARATOR" />
            <p :class="MENU_LABEL">
              {{ t("products.productFileManager.menuSectionManage") }}
            </p>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="downloadFolderArchive(contextMenu.item.id); closeContextMenu()"
            >
              <ArrowDownToLine class="size-4 text-muted-foreground" />
              {{ t("products.productFileManager.downloadArchiveAction") }}
            </Button>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="openShareDialog('folder', contextMenu.item); closeContextMenu()"
            >
              <Share2 class="size-4 text-muted-foreground" />
              {{ t("products.productFileManager.shareAction") }}
            </Button>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="startInlineRename('folder', contextMenu.item); closeContextMenu()"
            >
              <Pencil class="size-4 text-muted-foreground" />
              {{ t("products.productFileManager.renameAction") }}
              <MenuKbd class="ml-auto" :keys="['F2']" />
            </Button>
            <div :class="MENU_SEPARATOR" />
            <p :class="MENU_LABEL">
              {{ t("products.productFileManager.menuSectionDanger") }}
            </p>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal text-destructive hover:bg-destructive/10 hover:text-destructive"
              :class="MENU_ITEM"
              @click="openDeleteFolderDialog(contextMenu.item); closeContextMenu()"
            >
              <Trash2 class="size-4" />
              {{ t("common.delete") }}
              <MenuKbd class="ml-auto" :keys="['del']" />
            </Button>
          </template>

          <!-- File -->
          <template v-else-if="contextMenu.type === 'file'">
            <div class="min-w-0 px-2 py-1.5">
              <p class="truncate text-sm font-medium text-foreground">
                {{ contextMenu.item?.displayName || contextMenu.item?.fileName || contextMenu.item?.name }}
              </p>
            </div>
            <div :class="MENU_SEPARATOR" />
            <p :class="MENU_LABEL">
              {{ t("products.productFileManager.menuSectionManage") }}
            </p>
            <a
              class="w-full"
              :class="MENU_ITEM"
              :href="contextMenu.item.fullPath"
              target="_blank"
              @click="closeContextMenu()"
            >
              <ArrowDownToLine class="size-4 text-muted-foreground" />
              {{ t("common.download") }}
            </a>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="openShareDialog('file', contextMenu.item); closeContextMenu()"
            >
              <Share2 class="size-4 text-muted-foreground" />
              {{ t("products.productFileManager.shareAction") }}
            </Button>
            <Button
              v-if="contextMenu.item?.isPublic && contextMenu.item?.publicUrl"
              variant="ghost"
              class="h-auto w-full justify-start font-normal text-destructive hover:bg-destructive/10 hover:text-destructive"
              :class="MENU_ITEM"
              @click="
                shareTarget = { type: 'file', item: contextMenu.item };
                revokePublicAccess();
                closeContextMenu();
              "
            >
              <Globe class="size-4" />
              {{ t("products.productFileManager.stopPublicAccessAction") }}
            </Button>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="startInlineRename('file', contextMenu.item); closeContextMenu()"
            >
              <Pencil class="size-4 text-muted-foreground" />
              {{ t("products.productFileManager.renameAction") }}
              <MenuKbd class="ml-auto" :keys="['F2']" />
            </Button>
            <div :class="MENU_SEPARATOR" />
            <p :class="MENU_LABEL">
              {{ t("products.productFileManager.menuSectionDanger") }}
            </p>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal text-destructive hover:bg-destructive/10 hover:text-destructive"
              :class="MENU_ITEM"
              @click.stop="deleteSingleFile(contextMenu.item.id); closeContextMenu()"
            >
              <Trash2 class="size-4" />
              {{ t("common.delete") }}
              <MenuKbd class="ml-auto" :keys="['del']" />
            </Button>
          </template>

          <!-- Empty area -->
          <template v-else>
            <p :class="MENU_LABEL">
              {{ t("products.productFileManager.menuSectionCreate") }}
            </p>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="startInlineCreate(); closeContextMenu()"
            >
              <Plus class="size-4 text-muted-foreground" />
              {{ t("products.productFileManager.createFolderAction") }}
            </Button>
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="openFileDialog(); closeContextMenu()"
            >
              <Upload class="size-4 text-muted-foreground" />
              {{ t("products.productFileManager.uploadFilesAction") }}
            </Button>
            <div :class="MENU_SEPARATOR" />
            <Button
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="downloadFolderArchive(currentFolderId); closeContextMenu()"
            >
              <ArrowDownToLine class="size-4 text-muted-foreground" />
              {{ t("products.productFileManager.downloadArchiveAction") }}
            </Button>
          </template>
        </div>
      </div>
    </Teleport>

    <!-- Delete folder dialog -->
    <AlertDialog :open="showDeleteFolder" @update:open="showDeleteFolder = $event">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {{ t("products.productFileManager.deleteFolderTitle", { name: deleteFolderTarget?.name }) }}
          </AlertDialogTitle>
        </AlertDialogHeader>
        <div
          v-if="deleteFolderTarget && (deleteFolderTarget.fileCount > 0 || deleteFolderTarget.subfolderCount > 0)"
          class="flex w-full items-start gap-3 text-left"
        >
          <Checkbox
            :model-value="deleteFolderRecursive"
            @update:model-value="deleteFolderRecursive = $event"
            id="recursive-delete"
          />
          <label for="recursive-delete" class="text-sm cursor-pointer">
            {{ t("products.productFileManager.deleteFolderContentsLabel", {
              fileCount: deleteFolderTarget.totalFileCount ?? deleteFolderTarget.fileCount,
              subfolderCount: deleteFolderTarget.subfolderCount,
              size: formatBytes(deleteFolderTarget.totalSizeBytes ?? 0),
            }) }}
          </label>
        </div>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            @click="deleteFolder"
            :disabled="deleteFolderTarget && (deleteFolderTarget.fileCount > 0 || deleteFolderTarget.subfolderCount > 0) && !deleteFolderRecursive"
          >
            {{ t("common.delete") }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>

    <ShareLinkDialog
      v-model:open="showShareDialog"
      v-model:ttl-hours="shareTtlHours"
      :target-type="shareTarget?.type || 'file'"
      :item-name="shareItemName"
      :allow-permanent="shareTarget?.type === 'file'"
      :share-url="shareUrl"
      :creating="isCreatingShare"
      :copied="shareCopied"
      @create="createShareLink"
      @copy="copyShareUrl"
      @revoke="revokePublicAccess"
    />

    <!-- Sticky bottom actions -->
    <div class="flex shrink-0 items-center gap-2 border-t bg-background p-3 rounded-b-xl">
      <Button variant="outline" size="sm" @click="startInlineCreate">
        <Plus class="size-4 mr-1" />
        {{ t("products.productFileManager.folderTypeLabel") }}
      </Button>
      <Button size="sm" @click="openFileDialog">
        <Upload class="size-4 mr-1" />
        {{ t("common.upload") }}
      </Button>
      <Popover>
        <PopoverTrigger as-child>
          <Button size="sm" :disabled="selectedFileCount === 0">
            <Tag class="size-4 mr-1" />
            {{ t("products.productFileManager.tagLabel") }}
          </Button>
        </PopoverTrigger>
        <PopoverContent align="start" class="w-56 p-0">
          <div class="p-1 max-h-48 overflow-y-auto">
            <Button
              v-for="tag in storeTags"
              :key="tag.id"
              type="button"
              variant="ghost"
              class="h-auto w-full justify-start font-normal"
              :class="MENU_ITEM"
              @click="bulkToggleTag(tag.id)"
            >
              <span :class="['size-5 rounded-full shrink-0', tagBgClass(tag.color)]" />
              <span class="truncate flex-1 text-left">{{ tag.name }}</span>
              <Check v-if="bulkAllHaveTag(tag.id)" class="size-4 text-primary shrink-0" />
            </Button>
            <div v-if="storeTags.length === 0" class="text-sm text-muted-foreground text-center py-4">{{ t("products.productFileManager.noTagsLabel") }}</div>
          </div>
        </PopoverContent>
      </Popover>
      <Button v-if="isUploading" variant="destructive" size="sm" @click="cancelAllUploads">
        <X class="size-4 mr-1" />
        {{ t("products.productFileManager.cancelUploadsAction") }}
      </Button>
      <Button variant="outline" size="sm" @click="downloadFolderArchive(currentFolderId)" :disabled="isDownloadingArchive">
        <Spinner v-if="isDownloadingArchive && downloadingFolderId === currentFolderId" class="mr-1" />
        <ArrowDownToLine v-else class="size-4 mr-1" />
        {{ t("products.productFileManager.downloadArchiveAction") }}
      </Button>
      <template v-if="selection.size > 0">
        <span class="ml-auto text-sm text-muted-foreground tabular-nums">{{ t("products.productFileManager.selectedCount", { count: selection.size }) }}</span>
        <Button variant="outline" size="sm" @click="clearSelection">
          <X class="size-4 mr-1" />
          {{ t("products.productFileManager.deselectAction") }}
        </Button>
        <Button variant="outline" size="sm" @click="deleteSelected">
          <Trash2 class="size-4 mr-1" />
          {{ t("common.delete") }}
        </Button>
      </template>
    </div>
  </div>
</template>
