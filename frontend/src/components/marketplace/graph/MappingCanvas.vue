<script setup>
import {
  ref,
  computed,
  onMounted,
  onBeforeUnmount,
  watch,
  nextTick,
} from "vue";
import { useStore } from "vuex";
import { VueFlow, useVueFlow } from "@vue-flow/core";
import { Background } from "@vue-flow/background";
import { Controls } from "@vue-flow/controls";
import { MiniMap } from "@vue-flow/minimap";
import Button from "@/components/ui/button/Button.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import {
  Save,
  LayoutGrid,
  Maximize2,
  Minimize2,
  AlertTriangle,
  Loader2,
} from "lucide-vue-next";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import NodeContextMenu from "./NodeContextMenu.vue";
import NodeInspector from "./NodeInspector.vue";
import SourceNode from "./nodes/SourceNode.vue";
import ModifierNode from "./nodes/ModifierNode.vue";
import OpNode from "./nodes/OpNode.vue";
import AdapterFieldsNode from "./nodes/AdapterFieldsNode.vue";
import {
  autoMigrate,
  validateGraph,
  genId,
  createSourceNode,
  createModifierNode,
  createOpNode,
  analyzeFields,
  nodeOutputType,
  nodeInputType,
  firstInputHandle,
  ADAPTER_NODE_ID,
} from "./graphModel.js";
import { layoutGraph } from "./graphLayout.js";

import "@vue-flow/core/dist/style.css";
import "@vue-flow/core/dist/theme-default.css";
import "@vue-flow/controls/dist/style.css";
import "@vue-flow/minimap/dist/style.css";

const props = defineProps({
  connectionId: { type: [Number, String], required: true },
  mappingId: { type: [Number, String], required: true },
  fieldMappings: { type: Array, default: () => [] },
  availableSources: { type: Object, default: () => ({}) },
  previewProductId: { type: [Number, String], default: null },
});
const emit = defineEmits(["preview-result"]);

const store = useStore();
const vf = useVueFlow();
const {
  onConnect,
  onConnectStart,
  onConnectEnd,
  findNode,
  addEdges,
  addNodes,
  removeNodes,
  removeEdges,
  setNodes,
  setEdges,
  updateNodeData,
  toObject,
  fitView,
  onNodeClick,
  onPaneClick,
  onNodesChange,
  onEdgesChange,
  getSelectedNodes,
  addSelectedNodes,
  removeSelectedNodes,
  nodes,
  edges,
} = vf;

const flowWrap = ref(null);
const loading = ref(true);
const saving = ref(false);
const previewing = ref(false);
const selected = ref(null);
const menu = ref({
  open: false,
  left: 0,
  top: 0,
  clientX: 0,
  clientY: 0,
  connectType: null,
  connectDir: null,
});
// Tracks an in-progress wire drag started from an output pin (for drag-to-empty → node menu).
const pendingConnect = ref(null);
const connectionMade = ref(false);
const lastMouse = ref({ x: 0, y: 0 });
const rightDown = ref(null);
const rightMoved = ref(false);
const maximized = ref(false);

const issues = computed(() => validateGraph(nodes.value, edges.value));
const errorCount = computed(
  () => issues.value.filter((i) => i.level === "error").length,
);
const warningCount = computed(
  () => issues.value.filter((i) => i.level === "warning").length,
);
// Per-field import analysis, mirrored into the adapter node for its per-row export-only marker.
function refreshAnalysis() {
  const { analysis } = analyzeFields(nodes.value, edges.value);
  updateNodeData(ADAPTER_NODE_ID, { analysis });
}

// ---- Option resolution for valueMap/optionMap editors ----

const attrOptionsCache = ref({});
const loadingOptions = ref({});

const isMapModifier = (n) =>
  n?.type === "modifier" &&
  (n.data?.modifierType === "valueMap" || n.data?.modifierType === "optionMap");

// Walk upstream to the originating attribute (for source options).
function traceBackAttr(startId) {
  const byId = new Map(nodes.value.map((n) => [n.id, n]));
  const incoming = new Map();
  edges.value.forEach((e) => {
    const a = incoming.get(e.target) || [];
    a.push(e);
    incoming.set(e.target, a);
  });
  const seen = new Set();
  const stack = [startId];
  while (stack.length) {
    const id = stack.pop();
    if (seen.has(id)) continue;
    seen.add(id);
    const n = byId.get(id);
    if (
      n &&
      id !== startId &&
      (n.type === "attribute" || n.type === "storeAttribute")
    ) {
      return {
        kind: n.type === "attribute" ? "category" : "store",
        id: n.data?.attributeId,
      };
    }
    (incoming.get(id) || []).forEach((e) => stack.push(e.source));
  }
  return null;
}

// Walk downstream to the adapter field this branch feeds (for marketplace options).
function traceDownField(startId) {
  const byId = new Map(nodes.value.map((n) => [n.id, n]));
  const outgoing = new Map();
  edges.value.forEach((e) => {
    const a = outgoing.get(e.source) || [];
    a.push(e);
    outgoing.set(e.source, a);
  });
  const seen = new Set();
  const stack = [startId];
  while (stack.length) {
    const id = stack.pop();
    if (seen.has(id)) continue;
    seen.add(id);
    for (const e of outgoing.get(id) || []) {
      const t = byId.get(e.target);
      if (
        t &&
        t.type === "adapterFields" &&
        String(e.targetHandle).startsWith("f:")
      ) {
        return String(e.targetHandle).slice(2);
      }
      stack.push(e.target);
    }
  }
  return null;
}

function loadSourceOptions(kind, id) {
  const cacheKey = `${kind}:${id}`;
  if (attrOptionsCache.value[cacheKey] || loadingOptions.value[cacheKey])
    return;
  loadingOptions.value[cacheKey] = true;
  store.dispatch("getAttributeOptions", {
    type: kind,
    id,
    onSuccess: (response) => {
      const d = response?.data ?? {};
      let parsed = [];
      if (d.options) {
        try {
          parsed = JSON.parse(d.options);
        } catch {
          parsed = [];
        }
      }
      attrOptionsCache.value = {
        ...attrOptionsCache.value,
        [cacheKey]: parsed,
      };
      loadingOptions.value[cacheKey] = false;
    },
    onError: () => {
      loadingOptions.value[cacheKey] = false;
    },
  });
}

const selectedExternalOptions = computed(() => {
  if (!isMapModifier(selected.value)) return [];
  const key = traceDownField(selected.value.id);
  if (!key) return [];
  const fm = (props.fieldMappings || []).find(
    (f) => f.externalFieldKey === key,
  );
  return fm?.parsedOptions || [];
});

const selectedSourceOptions = computed(() => {
  if (!isMapModifier(selected.value)) return [];
  const attr = traceBackAttr(selected.value.id);
  if (!attr || !attr.id) return [];
  return attrOptionsCache.value[`${attr.kind}:${attr.id}`] || [];
});

watch(selected, (n) => {
  if (!isMapModifier(n)) return;
  const attr = traceBackAttr(n.id);
  if (attr && attr.id) loadSourceOptions(attr.kind, attr.id);
});

onConnect((conn) => {
  connectionMade.value = true;
  const handle = conn.targetHandle || "in";
  const dupes = edges.value.filter(
    (e) => e.target === conn.target && (e.targetHandle || "in") === handle,
  );
  if (dupes.length) removeEdges(dupes.map((e) => e.id));
  addEdges([{ ...conn, id: genId("e") }]);
  schedulePreview();
});

// Drag a wire from a pin: remember it so a drop on empty space can offer matching blocks.
// From an output pin ⇒ offer blocks that ACCEPT its type and wire into them.
// From an input pin  ⇒ offer blocks that PRODUCE its expected type and wire out of them.
onConnectStart((params) => {
  const p = params || {};
  const node = findNode(p.nodeId);
  const dir = p.handleType === "target" ? "target" : "source";
  pendingConnect.value = {
    nodeId: p.nodeId,
    handleId: p.handleId,
    dir,
    type:
      dir === "target" ? nodeInputType(node, p.handleId) : nodeOutputType(node),
  };
  connectionMade.value = false;
});

// Dropped on empty canvas (no successful connection) ⇒ open the filtered node menu at the cursor.
onConnectEnd((e) => {
  if (connectionMade.value || !pendingConnect.value) {
    pendingConnect.value = null;
    return;
  }
  const pt = e?.changedTouches?.[0] || e;
  const x = pt?.clientX;
  const y = pt?.clientY;
  const rect = flowWrap.value?.getBoundingClientRect();
  if (
    x == null ||
    y == null ||
    !rect ||
    x < rect.left ||
    x > rect.right ||
    y < rect.top ||
    y > rect.bottom
  ) {
    pendingConnect.value = null;
    return;
  }
  openConnectMenuAt(x, y, pendingConnect.value.type, pendingConnect.value.dir);
});

onNodeClick(({ node }) => {
  selected.value = node;
  closeMenu();
});
onPaneClick(() => {
  selected.value = null;
  closeMenu();
});

onMounted(() => {
  window.addEventListener("keydown", onKeydown);
  // Capture phase: fires even while Vue Flow captures pointer events on the pane.
  document.addEventListener("pointerdown", onPointerDown, true);
  document.addEventListener("pointermove", onPointerMove, true);
  load();
});
onBeforeUnmount(() => {
  window.removeEventListener("keydown", onKeydown);
  document.removeEventListener("pointerdown", onPointerDown, true);
  document.removeEventListener("pointermove", onPointerMove, true);
});
watch(() => props.mappingId, load);
watch(() => props.previewProductId, schedulePreview);
// Only react to structural changes (add/remove — e.g. the Delete key), not position/select/data,
// to avoid a feedback loop with preview's own updateNodeData() calls.
onNodesChange((changes) => {
  if ((changes || []).some((c) => c.type === "remove" || c.type === "add")) {
    refreshAnalysis();
    schedulePreview();
  }
});
onEdgesChange((changes) => {
  if ((changes || []).some((c) => c.type === "remove" || c.type === "add")) {
    refreshAnalysis();
    schedulePreview();
  }
});

async function load() {
  if (!props.connectionId || !props.mappingId) return; // ids arrive after parent mounted()
  loading.value = true;
  try {
    const data = await store.dispatch("getMappingGraph", {
      connectionId: props.connectionId,
      mappingId: props.mappingId,
    });
    let graph;
    let migrated = false;
    if (
      data?.hasGraph &&
      data.graph &&
      Array.isArray(data.graph.nodes) &&
      data.graph.nodes.length
    ) {
      graph = data.graph;
      // If the saved graph has no position data, apply auto-layout so nodes don't stack at (0,0).
      if (!graph.nodes.some((n) => n.position?.x != null)) migrated = true;
    } else {
      graph = autoMigrate(props.fieldMappings, props.availableSources);
      migrated = true;
    }
    // Saved graphs keep the user's layout; auto-migrated ones get a compact layered layout.
    setNodes(
      migrated ? layoutGraph(graph.nodes, graph.edges) : graph.nodes || [],
    );
    setEdges(graph.edges || []);
    await nextTick();
    refreshAnalysis();
    fitView({ padding: 0.2 });
    schedulePreview();
  } catch (e) {
    displaySonnerError(e);
  } finally {
    loading.value = false;
  }
}

// ---- Context menu (ПКМ / Tab) ----

function onPointerDown(e) {
  if (e.button === 2) {
    rightDown.value = { x: e.clientX, y: e.clientY };
    rightMoved.value = false;
  }
}
function onPointerMove(e) {
  lastMouse.value = { x: e.clientX, y: e.clientY };
  if (e.buttons & 2 && rightDown.value) {
    const dx = e.clientX - rightDown.value.x;
    const dy = e.clientY - rightDown.value.y;
    if (dx * dx + dy * dy > 16) rightMoved.value = true; // >4px ⇒ right-drag (pan)
  }
}

function openMenuAt(clientX, clientY) {
  pendingConnect.value = null; // plain ПКМ/Tab — not a wire drop
  const rect = flowWrap.value?.getBoundingClientRect();
  menu.value = {
    open: true,
    clientX,
    clientY,
    left: clientX - (rect?.left || 0),
    top: clientY - (rect?.top || 0),
    connectType: null,
    connectDir: null,
  };
}
function openConnectMenuAt(clientX, clientY, type, dir) {
  const rect = flowWrap.value?.getBoundingClientRect();
  menu.value = {
    open: true,
    clientX,
    clientY,
    left: clientX - (rect?.left || 0),
    top: clientY - (rect?.top || 0),
    connectType: type || null,
    connectDir: dir || null,
  };
}
function closeMenu() {
  menu.value.open = false;
  pendingConnect.value = null;
}
function toggleMaximize() {
  maximized.value = !maximized.value;
  nextTick(() => fitView({ padding: 0.2 }));
}
function onContextMenu(e) {
  e.preventDefault();
  // Right-drag pans; only a right *click* (no movement) opens the menu.
  if (rightMoved.value) {
    rightMoved.value = false;
    return;
  }
  openMenuAt(e.clientX, e.clientY);
}
function onKeydown(e) {
  const t = e.target;
  if (
    t &&
    (t.tagName === "INPUT" || t.tagName === "TEXTAREA" || t.isContentEditable)
  )
    return; // don't hijack typing in the inspector / menu search

  // Clipboard shortcuts (Ctrl/Cmd + C / V / D). Use e.code (physical key) so they work
  // regardless of the active keyboard layout — with a Cyrillic layout e.key would be "с"/"м"/"в".
  if (e.ctrlKey || e.metaKey) {
    if (e.code === "KeyC") {
      if (copySelection()) e.preventDefault();
    } else if (e.code === "KeyV") {
      if (pasteClipboard()) e.preventDefault();
    } else if (e.code === "KeyD") {
      e.preventDefault();
      duplicateSelection();
    }
    return;
  }

  if (e.key !== "Tab") return;
  const rect = flowWrap.value?.getBoundingClientRect();
  if (!rect) return;
  const { x, y } = lastMouse.value;
  if (x < rect.left || x > rect.right || y < rect.top || y > rect.bottom)
    return; // only over canvas
  e.preventDefault();
  openMenuAt(x, y);
}

function toFlow(client) {
  if (vf.screenToFlowCoordinate) return vf.screenToFlowCoordinate(client);
  if (vf.project) return vf.project(client);
  return client;
}

function onMenuAdd(desc) {
  const pos = toFlow({ x: menu.value.clientX, y: menu.value.clientY });
  let node = null;
  if (desc.group === "source")
    node = createSourceNode(desc.kind, pos, desc.payload || {});
  else if (desc.group === "modifier")
    node = createModifierNode(desc.modifierType, pos);
  else if (desc.group === "op") node = createOpNode(desc.opType, pos);
  if (node) {
    addNodes([node]);
    // If this menu was opened by dropping a wire on empty space, auto-wire it to the new block.
    const pc = pendingConnect.value;
    if (pc) {
      const edge =
        pc.dir === "target"
          ? {
              // Dragged from an input pin ⇒ new block's output feeds that input.
              source: node.id,
              sourceHandle: "out",
              target: pc.nodeId,
              targetHandle: pc.handleId,
            }
          : {
              // Dragged from an output pin ⇒ new block's input receives that output.
              source: pc.nodeId,
              sourceHandle: pc.handleId,
              target: node.id,
              targetHandle: firstInputHandle(node),
            };
      addEdges([{ id: genId("e"), ...edge }]);
    }
    selected.value = node;
    schedulePreview();
  }
  pendingConnect.value = null;
}

// ---- Inspector ----

function onInspectorUpdate(patch) {
  if (!selected.value) return;
  updateNodeData(selected.value.id, patch);
  selected.value = {
    ...selected.value,
    data: { ...selected.value.data, ...patch },
  };
  schedulePreview();
}
function onInspectorDelete() {
  if (!selected.value || selected.value.deletable === false) return;
  removeNodes([selected.value.id]);
  selected.value = null;
  schedulePreview();
}

// ---- Save / preview ----

function buildGraph() {
  const obj = toObject();
  return {
    nodes: (obj.nodes || []).map((n) => ({
      id: n.id,
      type: n.type,
      position: n.position,
      deletable: n.deletable,
      data: cleanData(n.data),
    })),
    edges: (obj.edges || []).map((e) => ({
      id: e.id,
      source: e.source,
      target: e.target,
      sourceHandle: e.sourceHandle,
      targetHandle: e.targetHandle,
    })),
  };
}
function cleanData(d) {
  const rest = { ...(d || {}) };
  delete rest.preview;
  delete rest.previewValue;
  delete rest.analysis;
  return rest;
}

// ---- Clipboard: copy / paste / duplicate selected nodes ----

const clipboard = ref({ nodes: [], edges: [] });

// Selected, copyable nodes — prefer Vue Flow's selection, fall back to the inspector's node.
function currentSelection() {
  const fromVf = (getSelectedNodes?.value || []).filter(
    (n) => n.deletable !== false,
  );
  if (fromVf.length) return fromVf;
  if (selected.value && selected.value.deletable !== false) {
    return [findNode(selected.value.id) || selected.value];
  }
  return [];
}

// Snapshot the current selection (excluding the non-deletable adapter sink) + its internal edges.
function copySelection() {
  const sel = currentSelection();
  if (!sel.length) return false;
  const ids = new Set(sel.map((n) => n.id));
  clipboard.value = {
    nodes: sel.map((n) => ({
      id: n.id,
      type: n.type,
      position: { ...n.position },
      data: JSON.parse(JSON.stringify(cleanData(n.data))),
    })),
    edges: edges.value
      .filter((e) => ids.has(e.source) && ids.has(e.target))
      .map((e) => ({
        source: e.source,
        target: e.target,
        sourceHandle: e.sourceHandle,
        targetHandle: e.targetHandle,
      })),
  };
  return true;
}

// Materialize a clipboard snapshot as fresh nodes/edges (new ids), offset and selected.
function spawnCopies(clip, offset) {
  if (!clip.nodes.length) return false;
  const idMap = new Map();
  const newNodes = clip.nodes.map((n) => {
    const id = genId(n.type);
    idMap.set(n.id, id);
    return {
      id,
      type: n.type,
      position: { x: n.position.x + offset.x, y: n.position.y + offset.y },
      data: JSON.parse(JSON.stringify(n.data)),
      selected: true,
    };
  });
  const newEdges = clip.edges.map((e) => ({
    id: genId("e"),
    source: idMap.get(e.source),
    target: idMap.get(e.target),
    sourceHandle: e.sourceHandle,
    targetHandle: e.targetHandle,
  }));
  const prevSelected = getSelectedNodes?.value
    ? [...getSelectedNodes.value]
    : [];
  addNodes(newNodes);
  if (newEdges.length) addEdges(newEdges);
  // Move selection to the freshly created copies.
  const created = newNodes.map((n) => findNode(n.id)).filter(Boolean);
  if (prevSelected.length && typeof removeSelectedNodes === "function")
    removeSelectedNodes(prevSelected);
  if (created.length && typeof addSelectedNodes === "function")
    addSelectedNodes(created);
  selected.value = created.length === 1 ? created[0] : null;
  schedulePreview();
  return true;
}

function pasteClipboard() {
  return spawnCopies(clipboard.value, { x: 40, y: 40 });
}

function duplicateSelection() {
  if (copySelection()) spawnCopies(clipboard.value, { x: 40, y: 40 });
}

async function save() {
  saving.value = true;
  try {
    const { projections } = analyzeFields(nodes.value, edges.value);
    await store.dispatch("saveMappingGraph", {
      connectionId: props.connectionId,
      mappingId: props.mappingId,
      graph: buildGraph(),
      fields: projections,
    });
    displaySonnerSuccess("Граф сохранён");
  } catch (e) {
    displaySonnerError(e);
  } finally {
    saving.value = false;
  }
}

let previewTimer = null;
function schedulePreview() {
  if (previewTimer) clearTimeout(previewTimer);
  if (!props.previewProductId) return;
  previewTimer = setTimeout(runPreview, 400);
}

// Runs automatically (debounced) on graph changes when a preview product is selected.
async function runPreview() {
  if (!props.previewProductId || !props.connectionId || !props.mappingId)
    return;
  previewing.value = true;
  try {
    const data = await store.dispatch("previewMappingGraph", {
      connectionId: props.connectionId,
      mappingId: props.mappingId,
      productId: props.previewProductId,
      graph: buildGraph(),
    });
    const outputs = data?.outputs || {};
    const nodeValues = data?.nodeValues || {};
    nodes.value.forEach((n) => {
      if (n.type === "adapterFields")
        updateNodeData(n.id, { preview: outputs });
      else updateNodeData(n.id, { previewValue: nodeValues[n.id] ?? null });
    });
    emit("preview-result", { outputs, fieldTypes: data?.fieldTypes || {} });
  } catch (e) {
    displaySonnerError(e);
  } finally {
    previewing.value = false;
  }
}

// Re-run the layered auto-layout on the current graph.
function autoArrange() {
  const obj = toObject();
  setNodes(layoutGraph(obj.nodes, obj.edges));
  nextTick(() => fitView({ padding: 0.2 }));
}
</script>

<template>
  <div
    :class="[
      'flex min-h-0 overflow-hidden border bg-background',
      maximized ? 'fixed inset-0 z-50' : 'relative h-full w-full rounded-lg',
    ]"
  >
    <div
      ref="flowWrap"
      class="relative min-w-0 flex-1"
      @contextmenu="onContextMenu"
    >
      <div
        class="absolute left-2 top-2 z-10 flex items-center gap-1.5 rounded-md border bg-card/95 p-1 shadow-sm backdrop-blur"
      >
        <Button size="sm" :disabled="saving || errorCount > 0" @click="save">
          <Loader2 v-if="saving" class="mr-1.5 size-4 animate-spin" />
          <Save v-else class="mr-1.5 size-4" />
          Сохранить
        </Button>
        <span
          v-if="previewing"
          class="inline-flex items-center gap-1 px-1 text-xs text-muted-foreground"
        >
          <Loader2 class="size-3.5 animate-spin" /> предпросмотр…
        </span>
        <Button
          size="sm"
          variant="ghost"
          @click="autoArrange"
          title="Авторасстановка блоков"
        >
          <LayoutGrid class="mr-1.5 size-4" />
          Авто
        </Button>
        <Button
          size="sm"
          variant="ghost"
          @click="toggleMaximize"
          :title="maximized ? 'Свернуть' : 'На весь экран'"
        >
          <Minimize2 v-if="maximized" class="size-4" />
          <Maximize2 v-else class="size-4" />
        </Button>
        <span class="mx-1 h-5 w-px bg-border" />
        <Badge v-if="errorCount" variant="destructive" class="gap-1">
          <AlertTriangle class="size-3" /> {{ errorCount }}
        </Badge>
        <Badge v-if="warningCount" variant="outline" class="gap-1 text-warning">
          <AlertTriangle class="size-3" /> {{ warningCount }}
        </Badge>
      </div>

      <VueFlow
        :min-zoom="0.2"
        :max-zoom="1.75"
        :delete-key-code="['Delete', 'Backspace']"
        :selection-key-code="true"
        :pan-on-drag="[1, 2]"
        :only-render-visible-elements="true"
        :elevate-edges-on-select="false"
        fit-view-on-init
        class="vf-canvas"
      >
        <Background />
        <MiniMap pannable zoomable />
        <Controls :show-interactive="false" />

        <template #node-attribute="p"><SourceNode v-bind="p" /></template>
        <template #node-storeAttribute="p"><SourceNode v-bind="p" /></template>
        <template #node-productField="p"><SourceNode v-bind="p" /></template>
        <template #node-static="p"><SourceNode v-bind="p" /></template>
        <template #node-groupName="p"><SourceNode v-bind="p" /></template>
        <template #node-storeFileUrl="p"><SourceNode v-bind="p" /></template>
        <template #node-storeFileContent="p"
          ><SourceNode v-bind="p"
        /></template>
        <template #node-productFileByName="p"
          ><SourceNode v-bind="p"
        /></template>
        <template #node-productFileContentByName="p"
          ><SourceNode v-bind="p"
        /></template>
        <template #node-productFilesByExtension="p"
          ><SourceNode v-bind="p"
        /></template>
        <template #node-modifier="p"><ModifierNode v-bind="p" /></template>
        <template #node-formatString="p"><OpNode v-bind="p" /></template>
        <template #node-join="p"><OpNode v-bind="p" /></template>
        <template #node-makeArray="p"><OpNode v-bind="p" /></template>
        <template #node-arrayAppend="p"><OpNode v-bind="p" /></template>
        <template #node-splitToArray="p"><OpNode v-bind="p" /></template>
        <template #node-arrayIndex="p"><OpNode v-bind="p" /></template>
        <template #node-arrayLength="p"><OpNode v-bind="p" /></template>
        <template #node-ifElse="p"><OpNode v-bind="p" /></template>
        <template #node-not="p"><OpNode v-bind="p" /></template>
        <template #node-and="p"><OpNode v-bind="p" /></template>
        <template #node-or="p"><OpNode v-bind="p" /></template>
        <template #node-equals="p"><OpNode v-bind="p" /></template>
        <template #node-notEquals="p"><OpNode v-bind="p" /></template>
        <template #node-greaterThan="p"><OpNode v-bind="p" /></template>
        <template #node-lessThan="p"><OpNode v-bind="p" /></template>
        <template #node-contains="p"><OpNode v-bind="p" /></template>
        <template #node-isEmpty="p"><OpNode v-bind="p" /></template>
        <template #node-isValid="p"><OpNode v-bind="p" /></template>
        <template #node-adapterFields="p"
          ><AdapterFieldsNode v-bind="p"
        /></template>
      </VueFlow>

      <!-- context menu + click-away catcher -->
      <div
        v-if="menu.open"
        class="absolute inset-0 z-20"
        @click="closeMenu"
        @contextmenu.prevent="closeMenu"
      />
      <div
        v-if="menu.open"
        class="absolute z-30"
        :style="{ left: menu.left + 'px', top: menu.top + 'px' }"
      >
        <NodeContextMenu
          :available-sources="availableSources"
          :filter-type="menu.connectType"
          :connect-dir="menu.connectDir"
          @add="onMenuAdd"
          @close="closeMenu"
        />
      </div>

      <div
        v-if="loading"
        class="absolute inset-0 z-40 flex items-center justify-center bg-background/60"
      >
        <Loader2 class="size-6 animate-spin text-muted-foreground" />
      </div>
    </div>

    <NodeInspector
      :node="selected"
      :external-field-options="selectedExternalOptions"
      :source-field-options="selectedSourceOptions"
      :store-file-options="availableSources.storeFiles || []"
      @update="onInspectorUpdate"
      @delete="onInspectorDelete"
      @close="selected = null"
    />
  </div>
</template>

<!-- Global (non-scoped) styles: node cards render into the Vue Flow tree via slots, so scoped
     styles wouldn't reach them. Colors use the project's OKLCH var() tokens. -->
<style>
.vf-canvas {
  height: 100%;
  width: 100%;
}

.vf-card {
  position: relative;
  min-width: 150px;
  max-width: 220px;
  padding: 8px 10px;
  border-radius: 8px;
  border: 1px solid var(--border);
  background: var(--card);
  color: var(--foreground);
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.06);
  font-size: 12px;
}
.vf-card--selected {
  border-color: var(--primary);
  box-shadow: 0 0 0 2px var(--primary);
}
.vf-card--error {
  border-color: var(--destructive);
}

.vf-card__head {
  display: flex;
  align-items: center;
  gap: 6px;
}
.vf-card__kind {
  font-size: 10px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.02em;
  color: var(--muted-foreground);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.vf-card__warn {
  flex-shrink: 0;
  margin-left: auto;
  width: 13px;
  height: 13px;
  color: var(--warning, #d97706);
}
.vf-card__title {
  margin-top: 3px;
  font-weight: 600;
  font-size: 13px;
  line-height: 1.2;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.vf-card__title--sm {
  font-weight: 500;
  font-size: 12px;
  color: var(--muted-foreground);
}

.vf-card__pin {
  display: flex;
  align-items: center;
  gap: 5px;
  margin-top: 5px;
}
.vf-card__pin--right {
  justify-content: flex-end;
}
.vf-dot {
  width: 8px;
  height: 8px;
  border-radius: 9999px;
  display: inline-block;
}
.vf-pin-label {
  font-size: 10px;
  color: var(--muted-foreground);
}
.vf-req {
  color: var(--destructive);
  font-weight: 700;
}

.vf-combine__inputs {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 6px;
}
.vf-iconbtn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  border-radius: 4px;
  border: 1px solid var(--border);
  color: var(--muted-foreground);
}
.vf-iconbtn:hover {
  background: var(--accent);
}

/* Single "Поля адаптера" sink node — metrics must match HEADER/ROW in AdapterFieldsNode.vue. */
.vf-card--adapter {
  min-width: 230px;
  max-width: 300px;
  padding: 0;
}
.vf-adapter__head {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 10px;
  border-bottom: 1px solid var(--border);
}
.vf-adapter__row {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 24px;
  padding: 0 10px;
  font-size: 12px;
}
.vf-adapter__row:hover {
  background: var(--accent);
}
.vf-adapter__label {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.vf-adapter__type {
  font-size: 10px;
  color: var(--muted-foreground);
}
.vf-adapter__marker {
  flex-shrink: 0;
  width: 12px;
  height: 12px;
}
.vf-adapter__marker--warn {
  color: var(--warning, #d97706);
}
.vf-adapter__marker--muted {
  color: var(--muted-foreground);
}
.vf-adapter__preview {
  max-width: 96px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: ui-monospace, monospace;
  font-size: 11px;
  color: var(--muted-foreground);
}

/* Windows-style marquee selection rectangle */
.vue-flow__selection,
.vue-flow__nodesselection-rect {
  background: rgba(59, 130, 246, 0.12);
  border: 1px solid rgba(59, 130, 246, 0.8);
}

/* Vue Flow handle (pin) sizing */
.vue-flow__handle {
  width: 9px;
  height: 9px;
  border: 2px solid var(--card);
  background: var(--muted-foreground);
}
</style>
