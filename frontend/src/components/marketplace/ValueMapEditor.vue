<script>
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import { Button } from "@/components/ui/button";
import SearchableSelect from "@/components/ui/searchable-select/SearchableSelect.vue";
import { Plus, Trash2, ArrowRight, AlertTriangle } from "lucide-vue-next";

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
    defaultValue: {
      handler(v) {
        this.localDefault = v != null ? String(v) : "";
      },
      immediate: true,
    },
  },
  methods: {
    normalizeRows(rows) {
      return (rows || []).map((m) => ({
        from: normalizeOptionValue(m.from, this.sourceFieldOptions),
        to: m.to != null ? String(m.to) : "",
      }));
    },
    valueIdLabel(valueId) {
      if (!valueId) return null;
      const opt = (this.externalFieldOptions || []).find(
        (o) => String(o.value) === String(valueId),
      );
      return opt?.label || null;
    },
    isBrokenRow(row) {
      if (!row.from || !this.sourceOptions.length) return false;
      return !this.sourceOptions.some((o) => o.value === String(row.from));
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
      this.localMappings[index].to = value != null ? String(value) : "";
      this.emitMappings();
    },
    onDefaultChange(value) {
      this.localDefault = value != null ? String(value) : "";
      this.$emit("update:defaultValue", this.localDefault);
    },
    emitMappings() {
      this.$emit("update:mappings", [...this.localMappings]);
    },
  },
};
</script>

<template>
  <div class="space-y-2">
    <Label class="text-xs font-medium">{{ $t("marketplace.valueMapEditor.title") }}</Label>

    <p
      v-if="!sourceOptions.length"
      class="text-[11px] text-muted-foreground"
    >
      {{ $t("marketplace.valueMapEditor.sourceOptionsHint") }}
    </p>
    <p class="text-[11px] text-muted-foreground">
      {{ $t("marketplace.valueMapEditor.valueIdHint") }}
    </p>

    <div
      v-for="(row, index) in localMappings"
      :key="index"
      class="flex items-center gap-1"
    >
      <SearchableSelect
        class="flex-1 h-7 text-xs"
        :class="{ 'ring-1 ring-warning': isBrokenRow(row) }"
        :options="sourceOptions"
        :model-value="row.from || ''"
        :disabled="!sourceOptions.length"
        @update:modelValue="(v) => onFromChange(index, v)"
        :placeholder="$t('marketplace.valueMapEditor.sourceOptionPlaceholder')"
        :search-threshold="3"
      />
      <ArrowRight class="size-3 shrink-0 text-muted-foreground" />
      <div class="flex-1 space-y-0.5">
        <Input
          :model-value="row.to"
          @update:modelValue="(v) => onToChange(index, v)"
          placeholder="valueId"
          class="h-7 text-xs font-mono"
        />
        <p
          v-if="valueIdLabel(row.to)"
          class="text-[10px] text-muted-foreground truncate"
          :title="valueIdLabel(row.to)"
        >
          {{ valueIdLabel(row.to) }}
        </p>
      </div>
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
      {{ $t("marketplace.valueMapEditor.addMapping") }}
    </Button>

    <div
      v-if="unmappedOptions.length > 0"
      class="flex items-start gap-1.5 text-xs text-warning-foreground bg-warning/10 rounded px-2 py-1.5"
    >
      <AlertTriangle class="size-3 shrink-0 mt-0.5" />
      <span>
        {{ $t("marketplace.valueMapEditor.unmappedWarning", { mapped: unmappedOptions.length, total: sourceFieldOptions.length }) }}
      </span>
    </div>

    <div class="space-y-1 pt-1 border-t">
      <Label class="text-xs">{{ $t("marketplace.valueMapEditor.defaultValueLabel") }}</Label>
      <Input
        :model-value="localDefault"
        @update:modelValue="onDefaultChange"
        :placeholder="$t('marketplace.valueMapEditor.defaultValuePlaceholder')"
        class="h-7 text-xs font-mono"
      />
      <p
        v-if="valueIdLabel(localDefault)"
        class="text-[10px] text-muted-foreground"
      >
        {{ valueIdLabel(localDefault) }}
      </p>
    </div>
  </div>
</template>