<script>
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Button from "@/components/ui/button/Button.vue";
import SearchableSelect from "@/components/ui/searchable-select/SearchableSelect.vue";
import { Plus, Trash2, ArrowRight, AlertTriangle } from "lucide-vue-next";

export default {
  components: { Input, Label, Button, SearchableSelect, Plus, Trash2, ArrowRight, AlertTriangle },
  props: {
    mappings: { type: Array, default: () => [] },
    externalFieldOptions: { type: Array, default: () => [] },
    sourceFieldOptions: { type: Array, default: () => [] },
    defaultValue: { type: String, default: "" },
  },
  emits: ["update:mappings", "update:defaultValue"],
  data() {
    return {
      localMappings: [],
      localDefault: "",
    };
  },
  computed: {
    selectOptions() {
      return this.externalFieldOptions.map((o) => ({
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
      const opts = this.sourceOptions.length > 0 ? this.sourceFieldOptions : this.externalFieldOptions;
      if (!opts?.length) return [];
      const mappedFroms = new Set(this.localMappings.map((m) => String(m.from)));
      return opts.filter((o) => !mappedFroms.has(String(o.value)));
    },
    totalOptions() {
      const opts = this.sourceOptions.length > 0 ? this.sourceFieldOptions : this.externalFieldOptions;
      return opts?.length || 0;
    },
  },
  watch: {
    mappings: {
      handler(v) {
        this.localMappings = (v || []).map((m) => ({ ...m }));
      },
      immediate: true,
    },
    defaultValue: {
      handler(v) {
        this.localDefault = v || "";
      },
      immediate: true,
    },
  },
  methods: {
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
    <Label class="text-xs font-medium">Соответствия значений</Label>

    <div
      v-for="(row, index) in localMappings"
      :key="index"
      class="flex items-center gap-1"
    >
      <!-- Source value: select or free text -->
      <SearchableSelect
        v-if="sourceOptions.length > 0"
        class="flex-1 h-7 text-xs"
        :options="sourceOptions"
        :model-value="row.from || ''"
        @update:modelValue="(v) => onFromChange(index, v)"
        placeholder="Исходное значение"
        :search-threshold="3"
      />
      <Input
        v-else
        :model-value="row.from"
        @update:modelValue="(v) => onFromChange(index, v)"
        placeholder="Исходное значение"
        class="h-7 text-xs flex-1"
      />
      <ArrowRight class="size-3 shrink-0 text-muted-foreground" />
      <!-- Target: select from marketplace options -->
      <SearchableSelect
        v-if="selectOptions.length > 0"
        class="flex-1 h-7 text-xs"
        :options="selectOptions"
        :model-value="row.to || ''"
        @update:modelValue="(v) => onToChange(index, v)"
        placeholder="Вариант маркетплейса"
        :search-threshold="3"
      />
      <Input
        v-else
        :model-value="row.to"
        @update:modelValue="(v) => onToChange(index, v)"
        placeholder="Результат"
        class="h-7 text-xs flex-1"
      />
      <Button
        variant="ghost"
        size="icon"
        class="size-6 shrink-0 text-destructive hover:text-destructive"
        @click="removeRow(index)"
      >
        <Trash2 class="size-3" />
      </Button>
    </div>

    <Button variant="outline" size="sm" class="w-full h-7 text-xs" @click="addRow">
      <Plus class="size-3 mr-1" />
      Добавить соответствие
    </Button>

    <div v-if="unmappedOptions.length > 0" class="flex items-start gap-1.5 text-[11px] text-amber-600 bg-amber-500/10 rounded px-2 py-1.5">
      <AlertTriangle class="size-3 shrink-0 mt-0.5" />
      <span>Не указаны соответствия для {{ unmappedOptions.length }} из {{ totalOptions }} значений</span>
    </div>

    <div v-if="selectOptions.length > 0" class="space-y-1 pt-1 border-t">
      <Label class="text-xs">Значение по умолчанию (если нет соответствия)</Label>
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
      <Label class="text-xs">Значение по умолчанию (если нет соответствия)</Label>
      <Input
        :model-value="localDefault"
        @update:modelValue="onDefaultChange"
        placeholder="Оставить как есть"
        class="h-7 text-xs"
      />
    </div>
  </div>
</template>
