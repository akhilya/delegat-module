<script>
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Button from "@/components/ui/button/Button.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Plus, Trash2, ArrowRight, AlertTriangle } from "lucide-vue-next";

export default {
  components: { Input, Label, Button, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Plus, Trash2, ArrowRight, AlertTriangle },
  props: {
    mappings: { type: Array, default: () => [] },
    externalFieldOptions: { type: Array, default: () => [] },
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
    unmappedOptions() {
      if (!this.externalFieldOptions?.length) return [];
      const mappedFroms = new Set(this.localMappings.map((m) => String(m.from)));
      return this.externalFieldOptions.filter((o) => !mappedFroms.has(String(o.value)));
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
    getOptionLabel(value) {
      const opt = this.externalFieldOptions?.find((o) => o.value === value);
      return opt ? opt.label : null;
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
      <!-- Select from options when available -->
      <Select
        v-if="externalFieldOptions.length > 0"
        :model-value="row.from"
        @update:modelValue="(v) => onFromChange(index, v)"
      >
        <SelectTrigger size="sm" class="h-7 text-xs flex-1">
          <SelectValue placeholder="Выберите значение" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem
            v-for="opt in externalFieldOptions"
            :key="opt.value"
            :value="String(opt.value)"
            class="text-xs"
          >
            {{ opt.label || opt.value }}
          </SelectItem>
        </SelectContent>
      </Select>
      <!-- Plain input fallback -->
      <Input
        v-else
        :model-value="row.from"
        @update:modelValue="(v) => onFromChange(index, v)"
        placeholder="Исходное"
        class="h-7 text-xs flex-1"
      />
      <ArrowRight class="size-3 shrink-0 text-muted-foreground" />
      <Input
        :model-value="row.to"
        @update:modelValue="(v) => onToChange(index, v)"
        placeholder="Результат"
        class="h-7 text-xs flex-1"
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

    <div v-if="unmappedOptions.length > 0" class="flex items-start gap-1.5 text-xs text-warning-foreground bg-warning/10 rounded px-2 py-1.5">
      <AlertTriangle class="size-3 shrink-0 mt-0.5" />
      <span>Не указаны соответствия для {{ unmappedOptions.length }} из {{ externalFieldOptions.length }} значений</span>
    </div>

    <div class="space-y-1 pt-1 border-t">
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
