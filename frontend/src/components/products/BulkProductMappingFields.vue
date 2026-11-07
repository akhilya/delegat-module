<script setup>
import { computed, onBeforeUnmount, ref, useId, watch } from "vue";
import { http } from "@/main";
import { t } from "@/i18n";
import { displaySonnerError } from "@/store/sonnerHelper";
import { mappingDisplayName } from "@/lib/mappingDisplayName";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const props = defineProps({
  productIds: { type: Array, required: true },
  connectionId: { type: [Number, String], default: null },
  connections: { type: Array, default: () => [] },
  pickConnection: { type: Boolean, default: false },
  active: { type: Boolean, default: true },
  disabled: { type: Boolean, default: false },
});

const fieldId = useId();
const localConnectionId = ref(null);
const connectionId = computed(() =>
  props.pickConnection ? localConnectionId.value : props.connectionId,
);
const productIds = computed(() => [...new Set(props.productIds)]);
const products = ref([]);
const selections = ref({});
const loading = ref(false);
const ready = ref(false);
const failed = ref(false);
let requestId = 0;

const groups = computed(() => {
  const result = new Map();
  for (const product of products.value) {
    const key = product.categoryId ?? "none";
    if (!result.has(key))
      result.set(key, {
        key,
        name:
          product.categoryName || t("products.bulkAdapterDialog.noCategory"),
        products: [],
        mappings: product.availableMappings ?? [],
      });
    result.get(key).products.push(product);
  }
  return [...result.values()];
});
const skipped = computed(() =>
  groups.value
    .filter((group) => !group.mappings.length)
    .reduce((count, group) => count + group.products.length, 0),
);
const payload = computed(() =>
  groups.value.flatMap((group) => {
    const id = Number(selections.value[group.key]);
    if (!group.mappings.some((mapping) => mapping.id === id)) return [];
    return group.products.map((product) => ({
      productId: product.productId,
      categoryMappingId: id,
    }));
  }),
);
const canSave = computed(
  () =>
    props.active &&
    !!connectionId.value &&
    ready.value &&
    !loading.value &&
    payload.value.length > 0 &&
    payload.value.length === productIds.value.length - skipped.value,
);

function reset() {
  ++requestId;
  products.value = [];
  selections.value = {};
  loading.value = false;
  ready.value = false;
  failed.value = false;
}

watch(
  [() => props.active, connectionId, () => productIds.value.join(",")],
  () => {
    reset();
    if (!props.active) localConnectionId.value = null;
    else if (connectionId.value && productIds.value.length) load();
  },
  { immediate: true },
);
onBeforeUnmount(() => {
  ++requestId;
});

async function load() {
  const request = ++requestId;
  const ids = [...productIds.value];
  loading.value = true;
  failed.value = false;
  ready.value = false;
  try {
    const response = await http.post(
      `/marketplace/connections/${connectionId.value}/resolve-mappings`,
      { productIds: ids },
      { cache: false },
    );
    if (request !== requestId) return;
    const data = response.data?.value ?? response.data;
    const rows = data?.products ?? [];
    const resolved = new Set(rows.map((product) => product.productId));
    if (
      rows.length !== ids.length ||
      resolved.size !== ids.length ||
      !ids.every((id) => resolved.has(id))
    ) {
      failed.value = true;
      return;
    }
    products.value = rows;
    const initial = {};
    for (const group of groups.value) {
      const current = group.products[0]?.selectedMappingId;
      if (
        current &&
        group.products.every(
          (product) => product.selectedMappingId === current,
        ) &&
        group.mappings.some((mapping) => mapping.id === current)
      )
        initial[group.key] = String(current);
      else if (group.mappings.length === 1)
        initial[group.key] = String(group.mappings[0].id);
    }
    selections.value = initial;
    ready.value = true;
  } catch (error) {
    if (request !== requestId) return;
    failed.value = true;
    displaySonnerError(error);
  } finally {
    if (request === requestId) loading.value = false;
  }
}

async function save() {
  if (!canSave.value) return null;
  const selected = [...payload.value];
  const response = await http.post(
    `/marketplace/connections/${connectionId.value}/save-mapping-selections`,
    { selections: selected },
    { cache: false },
  );
  const data = response.data?.value ?? response.data;
  return data?.updated ?? selected.length;
}

function mappingLabel(mapping) {
  return mappingDisplayName(
    mapping,
    t("products.bulkAdapterDialog.mappingFallback", { id: mapping.id }),
  );
}

defineExpose({ canSave, save });
</script>

<template>
  <FieldGroup class="gap-4">
    <SettingsFormField
      v-if="pickConnection"
      :label="t('products.bulkAdapterDialog.adapterLabel')"
      :label-for="`${fieldId}-adapter`"
    >
      <Select
        v-model="localConnectionId"
        :disabled="disabled || !connections.length"
      >
        <SelectTrigger :id="`${fieldId}-adapter`" class="w-full min-w-0">
          <SelectValue
            :placeholder="t('products.bulkAdapterDialog.adapterPlaceholder')"
          />
        </SelectTrigger>
        <SelectContent>
          <SelectItem
            v-for="connection in connections"
            :key="connection.id"
            :value="connection.id"
            >{{ connection.name }}</SelectItem
          >
        </SelectContent>
      </Select>
      <p v-if="!connections.length" class="text-xs text-muted-foreground">
        {{ t("products.bulkEditDialog.noAdapters") }}
      </p>
    </SettingsFormField>
    <p class="text-xs text-muted-foreground">
      {{ t("products.bulkAdapterDialog.mappingHint") }}
    </p>
    <div
      v-if="loading"
      role="status"
      class="flex items-center gap-2 text-sm text-muted-foreground"
    >
      <Spinner />{{ t("products.bulkAdapterDialog.loadingMappings") }}
    </div>
    <div v-else-if="failed" role="alert" class="space-y-2">
      <p class="text-sm text-destructive">
        {{ t("products.bulkAdapterDialog.mappingLoadFailed") }}
      </p>
      <Button
        type="button"
        variant="outline"
        size="sm"
        :disabled="disabled"
        @click="load"
        >{{ t("common.retry") }}</Button
      >
    </div>
    <template v-else-if="ready">
      <p v-if="skipped" role="status" class="text-sm text-muted-foreground">
        {{
          t("products.bulkAdapterDialog.unmappedSkipped", { count: skipped })
        }}
      </p>
      <SettingsFormField
        v-for="group in groups"
        :key="group.key"
        :label="group.name"
        :label-for="`${fieldId}-${group.key}`"
        :description="
          t('products.bulkAdapterDialog.mappingGroupCount', {
            count: group.products.length,
          })
        "
      >
        <Select
          v-model="selections[group.key]"
          :disabled="disabled || !group.mappings.length"
        >
          <SelectTrigger :id="`${fieldId}-${group.key}`" class="w-full min-w-0">
            <SelectValue
              :placeholder="
                t(
                  group.mappings.length
                    ? 'products.bulkAdapterDialog.mappingPlaceholder'
                    : 'products.bulkAdapterDialog.noMappings',
                )
              "
            />
          </SelectTrigger>
          <SelectContent>
            <SelectItem
              v-for="mapping in group.mappings"
              :key="mapping.id"
              :value="String(mapping.id)"
              :text-value="mappingLabel(mapping)"
            >
              <span
                class="min-w-0 flex-1 truncate"
                :title="mappingLabel(mapping)"
                >{{ mappingLabel(mapping) }}</span
              >
            </SelectItem>
          </SelectContent>
        </Select>
      </SettingsFormField>
    </template>
  </FieldGroup>
</template>
