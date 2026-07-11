<script>
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import { Button } from "@/components/ui/button";
import SearchableSelect from "@/components/ui/searchable-select/SearchableSelect.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Plus, Trash2, ArrowRight, AlertTriangle } from "lucide-vue-next";

const NONE = "__none__";

function normalizeOptionValue(raw, options) {
  if (!raw || !options?.length) return raw || "";
  const s = String(raw);
  const byValue = options.find((o) => String(o.value) === s);
  if (byValue) return String(byValue.value);
  const byLabel = options.find(
    (o) =>
      String(o.label || "").toLowerCase() === s.toLowerCase() ||
      String(o.value || "").toLowerCase() === s.toLowerCase(),
  );
  return byLabel ? String(byLabel.value) : s;
}

export default {
  components: {
    Input,
    Label,
    Button,
    SearchableSelect,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Plus,
    Trash2,
    ArrowRight,
    AlertTriangle,
  },
  props: {
    mappings: { type: Array, default: () => [] },
    externalFieldOptions: { type: Array, default: () => [] },
    sourceFieldOptions: { type: Array, default: () => [] },
    persistedTargetOptions: { type: Array, default: () => [] },
    adapterFieldsWithOptions: { type: Array, default: () => [] },
    defaultValue: { type: String, default: "" },
  },
  emits: ["update:mappings", "update:defaultValue", "load-field-options"],
  data() {
    return {
      localMappings: [],
      localDefault: "",
      NONE,
    };
  },
  computed: {
    targetOptionsList() {
      if (this.persistedTargetOptions?.length) return this.persistedTargetOptions;
      return this.externalFieldOptions || [];
    },
    selectOptions() {
      return this.targetOptionsList.map((o) => ({
        value: String(o.value),
        label: o.label || o.value,
      }));
    },
    sourceOptions() {
      return this.sourceFieldOptions.map((o) => ({
        value: String(o.value),
        label: o.label || o.value,
      }));
    },
    unmappedOptions() {
      if (!this.sourceFieldOptions?.length) return [];
      const mappedFroms = new Set(this.localMappings.map((m) => String(m.from)));
      return this.sourceFieldOptions.filter((o) => !mappedFroms.has(String(o.value)));
    },
  },
  watch: {
    mappings: {
      handler(v) {
        this.localMappings = this.normalizeRows(v || []);
      },
      immediate: true,
    },
    sourceFieldOptions: {
      handler() {
        this.localMappings = this.normalizeRows(this.mappings || []);
      },
      deep: true,
    },
    targetOptionsList: {
      handler() {
        this.localMappings = this.normalizeRows(this.mappings || []);
      },
      deep: true,
    },
    defaultValue: {
      handler(v) {
        this.localDefault = normalizeOptionValue(v, this.targetOptionsList) || "";
      },
      immediate: true,
    },
  },
  methods: {
    normalizeRows(rows) {
      return (rows || []).map((m) => ({
        from: normalizeOptionValue(m.from, this.sourceFieldOptions),
        to: normalizeOptionValue(m.to, this.targetOptionsList),
      }));
    },
    onFieldPick(fieldKey) {
      if (!fieldKey || fieldKey === NONE) return;
      const f = this.adapterFieldsWithOptions.find((x) => x.fieldKey === fieldKey);
      if (f) this.$emit("load-field-options", f);
    },
    addRow() {
      this.localMappings.push({ from: "", to: "" });
      this.emitMappings();
    },
    removeRow(index) {
      this.localMappings.splice(index, 1);
      this.emitMappings();
    },
    onFromChange(index, value) {
      this.localMappings[index].from = value;
      this.emitMappings();
    },
    onToChange(index, value) {
      this.localMappings[index].to = value;
      this.emitMappings();
    },
    onDefaultChange(value) {
      this.localDefault = value;
      this.$emit("update:defaultValue", value);
    },
    emitMappings() {
      this.$emit("update:mappings", [...this.localMappings]);
    },
  },
};
</script>

<template>
  <div class="space-y-2">
    <Label class="text-xs font-medium">Соответствия: вариант → вариант адаптера</Label>

    <p
      v-if="!sourceOptions.length"
      class="text-[11px] text-muted-foreground"
    >
      Слева — варианты источника. Подключите блок к атрибуту категории с вариантами.
    </p>

    <div
      v-if="!selectOptions.length && adapterFieldsWithOptions.length"
      class="space-y-1"
    >
      <Select :model-value="NONE" @update:model-value="onFieldPick">
        <SelectTrigger class="h-7 text-xs">
          <SelectValue placeholder="Загрузить варианты из поля адаптера..." />
        </SelectTrigger>
        <SelectContent>
          <SelectItem
            v-for="f in adapterFieldsWithOptions"
            :key="f.fieldKey"
            :value="f.fieldKey"
            class="text-xs"
          >
            {{ f.label }}
          </SelectItem>
        </SelectContent>
      </Select>
      <p class="text-[11px] text-muted-foreground">
        Или подключите блок к полю адаптера — варианты подтянутся автоматически.
      </p>
    </div>

    <div
      v-for="(row, index) in localMappings"
      :key="index"
      class="flex items-center gap-1"
    >
      <SearchableSelect
        class="flex-1 h-7 text-xs"
        :options="sourceOptions"
        :model-value="row.from || ''"
        :disabled="!sourceOptions.length"
        @update:modelValue="(v) => onFromChange(index, v)"
        placeholder="Вариант источника"
        :search-threshold="3"
      />
      <ArrowRight class="size-3 shrink-0 text-muted-foreground" />
      <SearchableSelect
        class="flex-1 h-7 text-xs"
        :options="selectOptions"
        :model-value="row.to || ''"
        :disabled="!selectOptions.length"
        @update:modelValue="(v) => onToChange(index, v)"
        placeholder="Вариант адаптера"
        :search-threshold="3"
      />
      <Button
        variant="ghost"
        size="icon"
        class="size-7 shrink-0 text-destructive hover:text-destructive"
        @click="removeRow(index)"
      >
        <Trash2 class="size-3" />
      </Button>
    </div>

    <Button variant="outline" size="sm" class="w-full h-7 text-xs" @click="addRow">
      <Plus class="size-3 mr-1" />
      Добавить соответствие
    </Button>

    <div
      v-if="unmappedOptions.length > 0"
      class="flex items-start gap-1.5 text-xs text-warning-foreground bg-warning/10 rounded px-2 py-1.5"
    >
      <AlertTriangle class="size-3 shrink-0 mt-0.5" />
      <span>
        Не указаны соответствия для {{ unmappedOptions.length }} из
        {{ sourceFieldOptions.length }} значений категории
      </span>
    </div>

    <div v-if="selectOptions.length > 0" class="space-y-1 pt-1 border-t">
      <Label class="text-xs">Вариант по умолчанию (если нет соответствия)</Label>
      <SearchableSelect
        class="h-7 text-xs"
        :options="selectOptions"
        :model-value="localDefault || ''"
        @update:modelValue="onDefaultChange"
        placeholder="Оставить как есть"
        :search-threshold="3"
      />
    </div>
    <div v-else class="space-y-1 pt-1 border-t">
      <Label class="text-xs">По умолчанию (если нет соответствия)</Label>
      <Input
        :model-value="localDefault"
        @update:modelValue="onDefaultChange"
        placeholder="Оставить как есть"
        class="h-7 text-xs"
      />
    </div>
  </div>
</template>