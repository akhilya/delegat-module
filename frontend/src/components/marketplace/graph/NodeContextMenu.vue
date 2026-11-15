<script setup>
import { ref, computed, onMounted, nextTick } from "vue";
import { useI18n } from "vue-i18n";
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
import { getCachedMe } from "@/lib/meCache";
import {
  OP_SPECS,
  descriptorOutputType,
  typeCompatible,
  opLabel,
} from "./graphModel.js";
import { localizeStandardSourceName } from "./fieldLabels.js";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

const { t } = useI18n();

const props = defineProps({
  availableSources: { type: Object, default: () => ({}) },
  // When set (drag-from-pin), restrict to blocks that fit the dragged pin's type.
  // Null/"any" ⇒ show everything (plain right-click/Tab).
  filterType: { type: String, default: null },
  // "source" = dragged from an OUTPUT pin (offer consumers that accept the type);
  // "target" = dragged from an INPUT pin (offer producers whose output fits the type).
  connectDir: { type: String, default: null },
  // Adapter fields that carry predefined options (for standard-value search).
  adapterFieldsWithOptions: { type: Array, default: () => [] },
  // The adapter field whose input pin the menu was opened for (wire drag);
  // enables offering its option values directly.
  adapterValueField: { type: Object, default: null },
});
const emit = defineEmits(["add", "close"]);

const search = ref("");
const inputEl = ref(null);
onMounted(async () => {
  await nextTick();
  const el = inputEl.value?.$el ?? inputEl.value;
  el?.focus?.();
});

const q = computed(() => search.value.trim().toLowerCase());

// Word-level fuzzy match: a query word hits when it is a substring of a
// candidate word, or close enough to tolerate a typo (1-2 edits for longer
// words). Works the same for Cyrillic and Latin input.
const lev = (a, b) => {
  const n = b.length;
  let prev = Array.from({ length: n + 1 }, (_, i) => i);
  for (let i = 1; i <= a.length; i++) {
    const cur = [i];
    for (let j = 1; j <= n; j++) {
      cur[j] = Math.min(
        prev[j] + 1,
        cur[j - 1] + 1,
        prev[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1),
      );
    }
    prev = cur;
  }
  return prev[n];
};
const wordOk = (qw, cw) =>
  cw.includes(qw) ||
  (cw.length >= 3 && qw.startsWith(cw)) ||
  (qw.length >= 4 && lev(qw, cw) <= (qw.length >= 8 ? 2 : 1));
const match = (label, ...aliases) => {
  if (!q.value) return true;
  // Strip leading operator signs so "+15" still hits "15+", "возраст 15" etc.
  const qWords = q.value
    .split(/\s+/)
    .map((w) => w.replace(/^[+\-−×*x/÷:]+/, ""))
    .filter(Boolean);
  // Whole strings stay in the hay too: "S/M" or "10/12" are query words but
  // only the full label contains the slash.
  const hay = [label, ...aliases]
    .flatMap((a) => {
      const s = String(a || "").toLowerCase();
      return [s, ...s.split(/[\s,;/|]+/)];
    })
    .filter(Boolean);
  // A bare operator sign ("+", "/", "x") has no query words: only items with
  // that exact alias match, otherwise everything would be listed.
  if (!qWords.length) return hay.some((h) => h === q.value);
  return qWords.every((w) => hay.some((h) => wordOk(w, h)));
};

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
    if (match(label, ...aliases) && srcVisible(desc))
      out.push({ icon, label, desc });
  };
  (s.productFields || [])
    // The dedicated "Group name" source below covers the same value; offering
    // it again as a productField would create a duplicate block.
    .filter((f) => f.key !== "groupName")
    .forEach((f) =>
      push(
        Database,
        localizeStandardSourceName(f.key, f.displayName || f.key),
        {
          group: "source",
          kind: "productField",
          payload: f,
        },
        "product",
        f.key,
        f.displayName,
      ),
    );
  (s.stockFields || []).forEach((f) =>
    push(
      Database,
      localizeStandardSourceName(f.key, f.displayName || f.key),
      {
        group: "source",
        kind: "productField",
        payload: f,
      },
      "stock",
      f.key,
      f.displayName,
    ),
  );
  (s.priceFields || []).forEach((f) =>
    push(
      Database,
      localizeStandardSourceName(f.key, f.displayName || f.key),
      {
        group: "source",
        kind: "productField",
        payload: f,
      },
      "price",
      f.key,
      f.displayName,
    ),
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
  push(
    Boxes,
    t("marketplace.graph.contextMenu.groupNameItem"),
    { group: "source", kind: "groupName" },
    "group name",
    "group",
  );
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
    if (match(label, ...aliases) && srcVisible(desc))
      out.push({ icon, label, desc });
  };
  push(
    Hash,
    t("marketplace.graph.contextMenu.numberLiteral"),
    "number",
    "0",
    "number",
    "int",
    "float",
    "digit",
    "число",
    "цифра",
    "количество",
  );
  push(
    Type,
    t("marketplace.graph.contextMenu.textLiteral"),
    "string",
    "",
    "text",
    "string",
    "str",
    "текст",
    "строка",
    "слово",
  );
  push(
    ToggleLeft,
    t("marketplace.graph.contextMenu.booleanLiteral"),
    "boolean",
    "false",
    "boolean",
    "bool",
    "true",
    "false",
    "flag",
    "булево",
    "да",
    "нет",
    "флаг",
  );
  push(
    List,
    t("marketplace.graph.contextMenu.arrayLiteral"),
    "array",
    "",
    "array",
    "list",
    "arr",
    "массив",
    "список",
  );
  // Numeric query → a number literal prefilled with the typed value.
  const num = q.value.match(/^(-?\d+(?:[.,]\d+)?)$/);
  if (num) {
    const value = num[1].replace(",", ".");
    const desc = {
      group: "source",
      kind: "static",
      payload: {
        valueType: "number",
        staticValue: value,
        label: t("marketplace.graph.contextMenu.numberLiteral"),
      },
    };
    if (srcVisible(desc))
      out.push({
        icon: Hash,
        label: t("marketplace.graph.contextMenu.numberLiteral"),
        param: value,
        desc,
      });
  }
  // Adapter value: static node backed by the adapter field's parsedOptions.
  // Options are populated on connect or manually in the inspector, and the
  // bound field decides the real valueType - so offer it for any dragged pin.
  const adapterValueLabel = t("marketplace.graph.contextMenu.adapterValueItem");
  // Option labels of every adapter field act as aliases, so searching a known
  // dictionary value (e.g. "красный") still surfaces the standard-value block.
  const optionAliases = props.adapterFieldsWithOptions.flatMap((f) => [
    f.label,
    f.fieldKey,
    ...(f.options || []).slice(0, 50).flatMap((o) => [o.label, o.value]),
  ]);
  if (
    match(
      adapterValueLabel,
      "default",
      "enum",
      "preset",
      "select",
      "стандарт",
      "значение",
      "словарь",
      "вариант",
      ...optionAliases,
    ) &&
    srcVisible({
      group: "source",
      kind: "static",
      payload: { valueType: "any" },
    })
  ) {
    out.push({
      icon: ListChecks,
      label: adapterValueLabel,
      desc: {
        group: "source",
        kind: "static",
        payload: {
          isAdapterValue: true,
          valueType: "string",
          staticValue: "",
          label: "",
        },
      },
    });
  }
  return out;
});

// Values of a dictionary adapter field: each option creates a standard-value
// node already bound to that field. The field whose pin the menu was opened for
// comes first (shown even without a query); other fields appear while searching.
const optionItem = (field) => (o) => ({
  label: o.label,
  desc: {
    group: "source",
    kind: "static",
    payload: {
      isAdapterValue: true,
      valueType: field.valueType,
      staticValue: o.value,
      label: o.label,
      options: field.options,
      optionSourceKey: field.fieldKey,
      optionSourceLabel: field.label,
    },
  },
});
const adapterValueGroups = computed(() => {
  const groups = [];
  // Static sources have no input pin: skip them when the drag needs a consumer.
  if (wantConsumers.value) return groups;
  const groupLabel = (field) =>
    `${t("marketplace.graph.contextMenu.adapterValueItem")} · ${field.label}`;
  const pending = props.adapterValueField;
  if (pending?.options?.length) {
    const items = pending.options
      .filter((o) => match(o.label, o.value))
      .slice(0, 50)
      .map(optionItem(pending));
    if (items.length)
      groups.push({
        key: `av:${pending.fieldKey}`,
        label: groupLabel(pending),
        items,
      });
  }
  if (q.value) {
    for (const f of props.adapterFieldsWithOptions) {
      if (f.fieldKey === pending?.fieldKey || !f.options?.length) continue;
      const items = f.options
        .filter((o) => match(o.label, o.value))
        .slice(0, 10)
        .map(optionItem(f));
      if (items.length)
        groups.push({ key: `av:${f.fieldKey}`, label: groupLabel(f), items });
    }
  }
  return groups;
});

// "<sign>[number]" queries ("+15", "/ 15", "15+") → the arithmetic block with
// the operand prefilled; the argument is shown as a badge on the item.
const ARITH_SIGN_TO_OP = {
  "+": "add",
  "-": "subtract",
  "−": "subtract",
  "*": "multiply",
  x: "multiply",
  "×": "multiply",
  "/": "divide",
  "÷": "divide",
  ":": "divide",
};
const arithItems = computed(() => {
  const s = q.value;
  let sign = null;
  let num = null;
  let m = s.match(/^([+\-−*x×/÷:])\s*(-?\d+(?:[.,]\d+)?)$/);
  if (m) {
    sign = m[1];
    num = m[2];
  } else if ((m = s.match(/^(-?\d+(?:[.,]\d+)?)\s*([+\-−*x×/÷:])$/))) {
    num = m[1];
    sign = m[2];
  } else if ((m = s.match(/^([+\-−*x×/÷:])$/))) {
    sign = m[1];
  }
  const opType = sign && ARITH_SIGN_TO_OP[sign];
  if (!opType) return [];
  const spec = OP_SPECS[opType];
  if (!opVisible({ group: "op", opType }, spec?.accepts)) return [];
  const operand =
    num != null ? Number(num.replace(",", ".")) : spec.params[0].default;
  return [
    {
      label: opLabel(opType),
      param: num != null ? String(operand) : null,
      desc: { group: "op", opType, params: { operand } },
    },
  ];
});

// Product files referenced by name / extension (resolved per product at eval).
const productFileItems = computed(() => {
  const out = [];
  const push = (icon, label, kind, ...aliases) => {
    const desc = { group: "source", kind };
    if (match(label, ...aliases) && srcVisible(desc))
      out.push({ icon, label, desc });
  };
  push(
    Link,
    t("marketplace.graph.contextMenu.productFileByNameLink"),
    "productFileByName",
    "file",
    "url",
    "link",
    "image",
  );
  push(
    FileText,
    t("marketplace.graph.contextMenu.productFileContentByNameText"),
    "productFileContentByName",
    "file",
    "text",
    "content",
  );
  push(
    Files,
    t("marketplace.graph.contextMenu.productFilesByExtensionItem"),
    "productFilesByExtension",
    "extension",
    "ext",
    "files",
  );
  return out;
});

const storageItems = computed(() => {
  const s = props.availableSources;
  const files = s.storeFiles || [];
  const out = [];
  const push = (icon, label, kind, ...aliases) => {
    const desc = { group: "source", kind };
    if (match(label, ...aliases) && srcVisible(desc))
      out.push({ icon, label, desc });
  };
  // Pre-pick a file only when there's exactly one (otherwise chosen in the inspector).
  const only = files.length === 1 ? files[0] : null;
  push(
    HardDrive,
    t("marketplace.graph.contextMenu.storeFileUrlItem"),
    "storeFileUrl",
    "store",
    "storage",
    "file",
    "url",
  );
  push(
    FileText,
    t("marketplace.graph.contextMenu.storeFileContentItem"),
    "storeFileContent",
    "store",
    "storage",
    "file",
    "text",
  );
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
        match(opLabel(type), ...(sp.aliases || [])) &&
        opVisible({ group: "op", opType: type }, sp.accepts),
    )
    .map(([type, sp]) => ({
      icon: OP_ICONS[sp.icon] || Braces,
      label: opLabel(type),
      desc: { group: "op", opType: type },
    }));
const opItems = computed(() => opEntries(false));
const logicItems = computed(() => opEntries(true));

const modifierGroups = computed(() => {
  const byCat = {};
  const aiOn = getCachedMe()?.aiEnabled === true;
  Object.values(MODIFIER_CATALOG).forEach((m) => {
    if (m.type === "llmGenerate" && !aiOn) return;
    const desc = { group: "modifier", modifierType: m.type };
    if (
      match(m.displayName, ...(m.aliases || [])) &&
      opVisible(desc, m.applicableTypes)
    )
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
    ...adapterValueGroups.value,
    {
      key: "sources",
      label: t("marketplace.graph.contextMenu.groups.sources"),
      items: sourceItems.value,
    },
    {
      key: "literals",
      label: t("marketplace.graph.contextMenu.groups.literals"),
      items: literalItems.value,
    },
    {
      key: "storage",
      label: t("marketplace.graph.contextMenu.groups.storage"),
      items: storageItems.value,
    },
    {
      key: "productFiles",
      label: t("marketplace.graph.contextMenu.groups.productFiles"),
      items: productFileItems.value,
    },
    {
      key: "ops",
      label: t("marketplace.graph.contextMenu.groups.ops"),
      items: [
        ...arithItems.value,
        ...opItems.value.filter(
          (it) =>
            !arithItems.value.some((a) => a.desc.opType === it.desc.opType),
        ),
      ],
    },
    {
      key: "logic",
      label: t("marketplace.graph.contextMenu.groups.logic"),
      items: logicItems.value,
    },
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
    <Input
      ref="inputEl"
      v-model="search"
      type="search"
      :placeholder="t('marketplace.graph.contextMenu.searchPlaceholder')"
      class="h-auto rounded-none border-0 border-b border-border bg-transparent px-2.5 py-2 text-[13px] shadow-none focus-visible:ring-0"
      @keydown.esc="emit('close')"
    />
    <div class="vf-ctx__list thin-scrollbar">
      <template v-for="g in groups" :key="g.key">
        <Button
          variant="ghost"
          class="vf-ctx__group h-auto w-full justify-start"
          @click.stop="toggle(g)"
        >
          <component
            :is="isOpen(g) ? ChevronDown : ChevronRight"
            class="size-3"
          />
          {{ g.label }}
        </Button>
        <Button
          v-for="(it, i) in isOpen(g) ? g.items : []"
          :key="g.key + '-' + i"
          variant="ghost"
          class="vf-ctx__item h-auto w-full justify-start"
          @click="pick(it.desc)"
        >
          {{ it.label }}
          <span v-if="it.param" class="vf-ctx__param">{{ it.param }}</span>
        </Button>
      </template>

      <div v-if="empty" class="vf-ctx__empty">
        {{ t("marketplace.graph.contextMenu.empty") }}
      </div>
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
  border-radius: var(--radius-md);
  box-shadow: none;
  overflow: hidden;
}
.vf-ctx__list {
  overflow-y: auto;
  padding: 4px;
}
.vf-ctx__group {
  gap: 5px;
  margin-top: 2px;
  padding: 6px 8px 3px;
  border-radius: 0;
  font-size: 10px;
  font-weight: normal;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  color: var(--muted-foreground);
}
.vf-ctx__group:hover {
  color: var(--foreground);
  background: transparent;
}
.vf-ctx__item {
  gap: 8px;
  padding: 6px 8px;
  border-radius: var(--radius-sm);
  font-size: 13px;
  font-weight: normal;
  color: var(--foreground);
}
.vf-ctx__param {
  margin-left: auto;
  padding: 1px 6px;
  border-radius: var(--radius-xs);
  background: var(--muted);
  color: var(--muted-foreground);
  font-family: var(--font-mono, ui-monospace, monospace);
  font-size: 11px;
}
.vf-ctx__empty {
  padding: 12px;
  text-align: center;
  color: var(--muted-foreground);
  font-size: 13px;
}
</style>
