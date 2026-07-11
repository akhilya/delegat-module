<script>
import { MODIFIER_CATALOG } from "./modifierCatalog.js";
import Input from "@/components/ui/input/Input.vue";
import Label from "@/components/ui/label/Label.vue";
import Button from "@/components/ui/button/Button.vue";
import ValueMapEditor from "./ValueMapEditor.vue";
import { Check, X } from "lucide-vue-next";

export default {
  components: { Input, Label, Button, ValueMapEditor, Check, X },
  props: {
    modifier: { type: Object, required: true },
    externalFieldOptions: { type: Array, default: () => [] },
    sourceFieldOptions: { type: Array, default: () => [] },
  },
  emits: ["update", "close"],
  data() {
    return {
      localParams: {},
    };
  },
  computed: {
    catalogEntry() {
      return MODIFIER_CATALOG[this.modifier.type];
    },
    paramSchema() {
      return this.catalogEntry?.params || [];
    },
  },
  methods: {
    onParamChange(key, value) {
      this.localParams[key] = value;
    },
    applyChanges() {
      this.$emit("update", { ...this.modifier, params: { ...this.localParams } });
      this.$emit("close");
    },
    getParamValue(param) {
      return this.localParams[param.key] ?? param.default;
    },
  },
  mounted() {
    // Initialize local params from modifier
    this.localParams = { ...(this.modifier.params || {}) };
    // Fill defaults for missing params
    for (const param of this.paramSchema) {
      if (this.localParams[param.key] === undefined) {
        this.localParams[param.key] = param.default;
      }
    }
  },
};
</script>

<template>
  <div class="p-3 space-y-3 min-w-[280px]">
    <div class="flex items-center justify-between">
      <span class="font-medium text-sm">{{ catalogEntry?.displayName || modifier.type }}</span>
      <Button variant="ghost" size="icon" class="size-6" @click="$emit('close')">
        <X class="size-3" />
      </Button>
    </div>

    <template v-for="param in paramSchema" :key="param.key">
      <!-- ValueMap special editor -->
      <template v-if="param.type === 'valueMap'">
        <ValueMapEditor
          :mappings="localParams[param.key] || []"
          :external-field-options="externalFieldOptions"
          :source-field-options="sourceFieldOptions"
          :default-value="localParams['default'] || ''"
          @update:mappings="(v) => onParamChange(param.key, v)"
          @update:defaultValue="(v) => onParamChange('default', v)"
        />
      </template>

      <!-- Number param -->
      <template v-else-if="param.type === 'number'">
        <div class="space-y-1">
          <Label class="text-xs">{{ param.label }}</Label>
          <Input
            type="number"
            :model-value="getParamValue(param)"
            @update:modelValue="(v) => onParamChange(param.key, Number(v))"
            class="h-8 text-sm"
          />
        </div>
      </template>

      <!-- Text param (skip 'default' if valueMap handles it) -->
      <template v-else-if="param.type === 'text' && !(modifier.type === 'valueMap' && param.key === 'default')">
        <div class="space-y-1">
          <Label class="text-xs">{{ param.label }}</Label>
          <Input
            :model-value="getParamValue(param)"
            @update:modelValue="(v) => onParamChange(param.key, v)"
            class="h-8 text-sm"
          />
        </div>
      </template>
    </template>

    <Button size="sm" class="w-full" @click="applyChanges">
      <Check class="size-3 mr-1" />
      Применить
    </Button>
  </div>
</template>
