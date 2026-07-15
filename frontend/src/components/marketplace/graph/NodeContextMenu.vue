<script setup>
import { ref, computed, onMounted, nextTick } from "vue";
import {
  Database,
  Tag,
  Store,
  Type,
  Boxes,
  Wand2,
  Braces,
  List,
  ListChecks,
  HardDrive,
  FileText,
  Hash,
  ToggleLeft,
  Link,
  Files,
  GitBranch,
  Binary,
  Equal,
  ChevronRight,
  ChevronDown,
} from "lucide-vue-next";
import { MODIFIER_CATALOG, MODIFIER_CATEGORIES } from "../modifierCatalog.js";
import {
  OP_SPECS,
  descriptorOutputType,
  typeCompatible,
} from "./graphModel.js";

const props = defineProps({
  availableSources: { type: Object, default: () => ({}) },
  // When set (drag-from-pin), restrict to blocks that fit the dragged pin's type.
  // Null/"any" ⇒ show everything (plain ПКМ/Tab).
  filterType: { type: String, default: null },
  // "source" = dragged from an OUTPUT pin (offer consumers that accept the type);
  // "target" = dragged from an INPUT pin (offer producers whose output fits the type).
  connectDir: { type: String, default: null },
});
const emit = defineEmits(["add", "close"]);

const search = ref("");
const inputEl = ref(null);
onMounted(async () => {
  await nextTick();
  inputEl.value?.focus();
});

const q = computed(() => search.value.trim().toLowerCase());
const match = (label, ...aliases) =>
  !q.value || [label, ...aliases].some((a) => (a || "").toLowerCase().includes(q.value));

const connecting = computed(
  () => !!props.filterType && props.filterType !== "any",
);
const wantProducers = computed(
  () => connecting.value && props.connectDir === "target",
);
const wantConsumers = computed(
  () => connecting.value && props.connectDir !== "target",
);

// Consumer test: this block's input list accepts the dragged output type.
const acceptsType = (list) => list == null || list.includes(props.filterType);
// Producer test: this block's output can feed the dragged input type.
const producesType = (desc) =>
  typeCompatible(descriptorOutputType(desc), props.filterType);

// Sources have no input pin ⇒ never shown when offering consumers.
const srcVisible = (desc) =>
  wantConsumers.value ? false : !wantProducers.value || producesType(desc);
const opVisible = (desc, inputList) => {
  if (wantConsumers.value) return acceptsType(inputList);
  if (wantProducers.value) return producesType(desc);
  return true;
};

const sourceItems = computed(() => {
  const s = props.availableSources;
  const out = [];
  const push = (icon, label, desc, ...aliases) => {
    if (match(label, ...aliases) && srcVisible(desc)) out.push({ icon, label, desc });
  };
  (s.productFields || []).forEach((f) =>
    push(Database, f.displayName || f.key, {
      group: "source",
      kind: "productField",
      payload: f,
    }, "товар"),
  );
  (s.stockFields || []).forEach((f) =>
    push(Database, f.displayName || f.key, {
      group: "source",
      kind: "productField",
      payload: f,
    }, "остат", "stock", "склад"),
  );
  (s.priceFields || []).forEach((f) =>
    push(Database, f.displayName || f.key, {
      group: "source",
      kind: "productField",
      payload: f,
    }, "цен", "price"),
  );
  (s.categoryAttributes || []).forEach((a) =>
    push(Tag, a.name, { group: "source", kind: "attribute", payload: a }),
  );
  (s.storeAttributes || []).forEach((a) =>
    push(Store, a.name, {
      group: "source",
      kind: "storeAttribute",
      payload: a,
    }),
  );
  push(Boxes, "Название группы", { group: "source", kind: "groupName" }, "group name", "group");
  return out;
});

// Typed literal constants (backed by the `static` node + valueType).
const literalItems = computed(() => {
  const out = [];
  const push = (icon, label, valueType, staticValue, ...aliases) => {
    const desc = {
      group: "source",
      kind: "static",
      payload: { valueType, staticValue, label },
    };
    if (match(label, ...aliases) && srcVisible(desc)) out.push({ icon, label, desc });
  };
  push(Hash, "Число", "number", "0", "number", "int", "float", "цифра");
  push(Type, "Текст", "string", "", "text", "string", "str", "строка");
  push(ToggleLeft, "Логическое", "boolean", "false", "boolean", "bool", "true", "false", "flag", "флаг");
  push(List, "Список", "array", "", "array", "list", "массив", "arr");
  // Adapter value: static node backed by the adapter field's parsedOptions.
  // Options are populated on connect or manually in the inspector.
  if (match("Стандартное значение", "default", "enum", "preset", "select", "выбор") && srcVisible({ group: "source", kind: "static" })) {
    out.push({
      icon: ListChecks,
      label: "Стандартное значение",
      desc: { group: "source", kind: "static", payload: { isAdapterValue: true, valueType: "string", staticValue: "", label: "" } },
    });
  }
  return out;
});

// Product files referenced by name / extension (resolved per product at eval).
const productFileItems = computed(() => {
  const out = [];
  const push = (icon, label, kind, ...aliases) => {
    const desc = { group: "source", kind };
    if (match(label, ...aliases) && srcVisible(desc)) out.push({ icon, label, desc });
  };
  push(Link, "Файл товара по имени · ссылка", "productFileByName", "file", "url", "link", "image", "файл");
  push(FileText, "Файл товара по имени · текст", "productFileContentByName", "file", "text", "content", "файл", "содержимое");
  push(Files, "Файлы товара по расширению", "productFilesByExtension", "extension", "ext", "files", "расширение");
  return out;
});

const storageItems = computed(() => {
  const s = props.availableSources;
  const files = s.storeFiles || [];
  const out = [];
  const push = (icon, label, kind, ...aliases) => {
    const desc = { group: "source", kind };
    if (match(label, ...aliases) && srcVisible(desc)) out.push({ icon, label, desc });
  };
  // Pre-pick a file only when there's exactly one (otherwise chosen in the inspector).
  const only = files.length === 1 ? files[0] : null;
  push(HardDrive, "Файл магазина · ссылка", "storeFileUrl", "store", "storage", "хранилище", "file", "url");
  push(FileText, "Файл магазина · текст", "storeFileContent", "store", "storage", "хранилище", "file", "text");
  if (only)
    out.forEach(
      (it) =>
        (it.desc.payload = { id: only.Id ?? only.id, fileName: only.fileName }),
    );
  return out;
});

const OP_ICONS = { List, GitBranch, Binary, Equal, Braces };
const opEntries = (logic) =>
  Object.entries(OP_SPECS)
    .filter(
      ([type, sp]) =>
        (sp.category === "logic") === logic &&
        match(sp.label, ...(sp.aliases || [])) &&
        opVisible({ group: "op", opType: type }, sp.accepts),
    )
    .map(([type, sp]) => ({
      icon: OP_ICONS[sp.icon] || Braces,
      label: sp.label,
      desc: { group: "op", opType: type },
    }));
const opItems = computed(() => opEntries(false));
const logicItems = computed(() => opEntries(true));

const modifierGroups = computed(() => {
  const byCat = {};
  Object.values(MODIFIER_CATALOG).forEach((m) => {
    const desc = { group: "modifier", modifierType: m.type };
    if (match(m.displayName, ...(m.aliases || [])) && opVisible(desc, m.applicableTypes))
      (byCat[m.category] ||= []).push({
        icon: Wand2,
        label: m.displayName,
        desc,
      });
  });
  return Object.entries(byCat).map(([cat, items]) => ({
    key: `mod:${cat}`,
    label: MODIFIER_CATEGORIES[cat]?.label || cat,
    items,
  }));
});

// Unified, ordered, collapsible groups.
const groups = computed(() =>
  [
    { key: "sources", label: "Источники", items: sourceItems.value },
    { key: "literals", label: "Значения", items: literalItems.value },
    { key: "storage", label: "Хранилище", items: storageItems.value },
    {
      key: "productFiles",
      label: "Файлы товара",
      items: productFileItems.value,
    },
    { key: "ops", label: "Операции", items: opItems.value },
    { key: "logic", label: "Логика", items: logicItems.value },
    ...modifierGroups.value,
  ].filter((g) => g.items.length),
);

const empty = computed(() => groups.value.length === 0);

// Collapsed groups (by key). A search query force-expands everything.
const COLLAPSED_KEY = "vfCtxCollapsed";
const collapsed = ref(
  new Set(JSON.parse(localStorage.getItem(COLLAPSED_KEY) || "[]")),
);
const isOpen = (g) => !!q.value || !collapsed.value.has(g.key);
const toggle = (g) => {
  const next = new Set(collapsed.value);
  next.has(g.key) ? next.delete(g.key) : next.add(g.key);
  collapsed.value = next;
  localStorage.setItem(COLLAPSED_KEY, JSON.stringify([...next]));
};

const pick = (desc) => {
  emit("add", desc);
  emit("close");
};
</script>

<template>
  <div class="vf-ctx" @click.stop @contextmenu.prevent>
    <input
      ref="inputEl"
      v-model="search"
      placeholder="Поиск узлов…"
      class="vf-ctx__search"
      @keydown.esc="emit('close')"
    />
    <div class="vf-ctx__list thin-scrollbar">
      <template v-for="g in groups" :key="g.key">
        <button class="vf-ctx__group" type="button" @click.stop="toggle(g)">
          <component
            :is="isOpen(g) ? ChevronDown : ChevronRight"
            class="size-3"
          />
          {{ g.label }}
          <span class="vf-ctx__count">{{ g.items.length }}</span>
        </button>
        <button
          v-for="(it, i) in isOpen(g) ? g.items : []"
          :key="g.key + '-' + i"
          class="vf-ctx__item"
          @click="pick(it.desc)"
        >
          <component :is="it.icon" class="size-3.5 text-muted-foreground" />{{
            it.label
          }}
        </button>
      </template>

      <div v-if="empty" class="vf-ctx__empty">Ничего не найдено</div>
    </div>
  </div>
</template>

<style scoped>
.vf-ctx {
  width: 248px;
  max-height: 360px;
  display: flex;
  flex-direction: column;
  border: 1px solid var(--border);
  background: var(--popover, var(--card));
  border-radius: 8px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.18);
  overflow: hidden;
}
.vf-ctx__search {
  padding: 8px 10px;
  border: 0;
  border-bottom: 1px solid var(--border);
  background: transparent;
  color: var(--foreground);
  outline: none;
  font-size: 13px;
}
.vf-ctx__list {
  overflow-y: auto;
  padding: 4px;
}
.vf-ctx__group {
  display: flex;
  align-items: center;
  gap: 5px;
  width: 100%;
  margin-top: 2px;
  padding: 6px 8px 3px;
  font-size: 10px;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  text-align: left;
  color: var(--muted-foreground);
  cursor: pointer;
}
.vf-ctx__group:hover {
  color: var(--foreground);
}
.vf-ctx__count {
  margin-left: auto;
  font-variant-numeric: tabular-nums;
  opacity: 0.7;
}
.vf-ctx__item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 6px 8px;
  border-radius: 6px;
  font-size: 13px;
  text-align: left;
  color: var(--foreground);
}
.vf-ctx__item:hover {
  background: var(--accent);
}
.vf-ctx__empty {
  padding: 12px;
  text-align: center;
  color: var(--muted-foreground);
  font-size: 13px;
}
</style>
