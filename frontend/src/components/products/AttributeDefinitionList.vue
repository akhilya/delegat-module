<script setup>
import { computed, ref, watch } from "vue";
import { canAct, PERMS } from "@/lib/permissions";
import { getCachedMe } from "@/lib/meCache";
import { t } from "@/i18n";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  ATTRIBUTE_BULK_LIMIT,
  retainAttributeSelection,
} from "@/lib/attributeBulk";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Spinner } from "@/components/ui/spinner";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogCancel,
} from "@/components/ui/alert-dialog";
import { GripVertical, Trash2, ChevronRight } from "lucide-vue-next";
import ListSelectAllRow from "@/components/lists/ListSelectAllRow.vue";
import ListBulkActions from "@/components/lists/ListBulkActions.vue";
import draggable from "vuedraggable";
import AttributeBulkEditDialog from "./AttributeBulkEditDialog.vue";

const props = defineProps({
  modelValue: { type: Array, required: true },
  endpoint: { type: String, required: true },
  caption: { type: Function, required: true },
  disabled: { type: Boolean, default: false },
});
const emit = defineEmits(["update:modelValue", "open", "reorder"]);
const selectedIds = ref([]);
const deleteIds = ref([]);
const showEdit = ref(false);
const busy = ref(false);
const selectAllControl = ref(null);
let actionTrigger = null;
watch(
  () => showEdit.value || deleteIds.value.length > 0,
  (open) => {
    if (open) actionTrigger = document.activeElement;
  },
);
function restoreFocus(event) {
  event.preventDefault();
  if (actionTrigger?.isConnected) actionTrigger.focus();
  else selectAllControl.value?.focus();
}
const canManage = computed(() => canAct(getCachedMe(), PERMS.CatalogManage));
const locked = computed(() => busy.value || props.disabled);
const selected = computed(() => new Set(selectedIds.value));
const count = computed(() => selectedIds.value.length);
const overLimit = computed(() => count.value > ATTRIBUTE_BULK_LIMIT);
const allSelected = computed(
  () => props.modelValue.length > 0 && count.value === props.modelValue.length,
);
const selectionState = computed(() =>
  allSelected.value ? true : count.value ? "indeterminate" : false,
);
watch(
  () => props.modelValue.map((attribute) => attribute.id),
  () => {
    selectedIds.value = retainAttributeSelection(
      selectedIds.value,
      props.modelValue,
    );
  },
);
watch(
  () => props.endpoint,
  () => {
    selectedIds.value = [];
    deleteIds.value = [];
    showEdit.value = false;
  },
);
function toggleSelection(id, checked) {
  if (locked.value) return;
  selectedIds.value =
    checked === true
      ? [...new Set([...selectedIds.value, id])]
      : selectedIds.value.filter((value) => value !== id);
}
function selectAll(checked) {
  if (!locked.value)
    selectedIds.value =
      checked === true ? props.modelValue.map((attribute) => attribute.id) : [];
}
async function runAction(action, ids, patch = {}) {
  if (
    locked.value ||
    !canManage.value ||
    !ids.length ||
    ids.length > ATTRIBUTE_BULK_LIMIT
  )
    return;
  busy.value = true;
  const endpoint = props.endpoint;
  try {
    const payload = { attributeIds: [...ids], ...patch };
    if (action === "delete")
      await http.post(`${endpoint}/bulk-delete`, payload, { cache: false });
    else await http.patch(`${endpoint}/bulk`, payload, { cache: false });
    if (endpoint !== props.endpoint) return;
    const affected = new Set(ids);
    emit(
      "update:modelValue",
      action === "delete"
        ? props.modelValue.filter((attribute) => !affected.has(attribute.id))
        : props.modelValue.map((attribute) => {
            if (!affected.has(attribute.id)) return attribute;
            const updated = { ...attribute, ...patch };
            updated.showOnProductPage =
              updated.isRequired || updated.showOnProductPage;
            return updated;
          }),
    );
    selectedIds.value = selectedIds.value.filter((id) => !affected.has(id));
    deleteIds.value = [];
    showEdit.value = false;
    displaySonnerSuccess(
      t(
        `components.attributeDefinitions.${action === "delete" ? "deleted" : "updated"}`,
        { count: ids.length },
      ),
    );
  } catch (error) {
    displaySonnerError(error);
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <div class="space-y-3" :aria-busy="busy">
    <InsetGroup>
      <ListSelectAllRow
        v-if="canManage && modelValue.length"
        ref="selectAllControl"
        :model-value="selectionState"
        :disabled="locked"
        @update:model-value="selectAll"
      />
      <draggable
        :model-value="modelValue"
        item-key="id"
        handle=".drag-handle"
        ghost-class="opacity-50"
        class="flex flex-col gap-0.5"
        tag="div"
        :disabled="locked || !canManage"
        @update:model-value="emit('update:modelValue', $event)"
        @end="emit('reorder')"
      >
        <template #item="{ element: attribute }">
          <InsetGroupItem as="div" class="attribute-row gap-1 p-1">
            <div class="flex w-full min-w-0 items-center gap-1">
              <label
                v-if="canManage"
                class="flex size-9 shrink-0 cursor-pointer items-center justify-center"
                :aria-label="
                  t('components.attributeDefinitions.select', {
                    name: attribute.name,
                  })
                "
              >
                <Checkbox
                  :model-value="selected.has(attribute.id)"
                  :disabled="locked"
                  :aria-label="
                    t('components.attributeDefinitions.select', {
                      name: attribute.name,
                    })
                  "
                  @update:model-value="toggleSelection(attribute.id, $event)"
                />
              </label>
              <span
                v-if="canManage"
                aria-hidden="true"
                class="drag-handle flex size-8 shrink-0 cursor-grab items-center justify-center active:cursor-grabbing"
              >
                <GripVertical class="size-4 text-muted-foreground/70" />
              </span>
              <Button
                type="button"
                variant="ghost"
                class="h-auto min-w-0 flex-1 justify-start gap-2 px-2 py-2 text-left font-normal hover:bg-transparent"
                :disabled="locked"
                @click="emit('open', attribute)"
              >
                <span class="min-w-0 flex-1">
                  <span class="block truncate">
                    {{ attribute.name }}
                    <span
                      v-if="attribute.isRequired"
                      class="text-destructive"
                      :title="t('components.attributeDefinitions.isRequired')"
                      >*</span
                    >
                    <span
                      v-if="attribute.isUnique"
                      class="ml-1 text-[10px] font-medium uppercase tracking-wide text-muted-foreground"
                      >{{
                        t("components.attributeDefinitions.uniqueBadge")
                      }}</span
                    >
                  </span>
                  <span
                    class="mt-0.5 block truncate text-xs font-normal text-muted-foreground"
                    >{{ caption(attribute) }}</span
                  >
                </span>
                <ChevronRight class="size-4 text-muted-foreground/40" />
              </Button>
              <Button
                v-if="canManage"
                type="button"
                variant="ghost"
                size="icon-sm"
                reveal-on-row-hover
                class="attribute-delete shrink-0 text-destructive hover:bg-destructive/10 hover:text-destructive"
                :disabled="locked"
                :aria-label="
                  t('components.attributeDefinitions.deleteOne', {
                    name: attribute.name,
                  })
                "
                @click="deleteIds = [attribute.id]"
              >
                <Trash2 class="size-4" />
              </Button>
            </div>
          </InsetGroupItem>
        </template>
      </draggable>
    </InsetGroup>

    <ListBulkActions
      v-if="canManage"
      :count="count"
      editable
      :busy="busy"
      :disabled="props.disabled || overLimit"
      @clear="selectedIds = []"
      @edit="showEdit = true"
      @delete="deleteIds = [...selectedIds]"
    />
    <p v-if="overLimit" role="alert" class="px-1 text-xs text-destructive">
      {{
        t("components.attributeDefinitions.limit", {
          count: ATTRIBUTE_BULK_LIMIT,
        })
      }}
    </p>

    <AttributeBulkEditDialog
      v-model:open="showEdit"
      :count="count"
      :busy="busy"
      @close-auto-focus="restoreFocus"
      @submit="runAction('update', [...selectedIds], $event)"
    />
    <AlertDialog
      :open="deleteIds.length > 0"
      @update:open="!busy && !$event && (deleteIds = [])"
    >
      <AlertDialogContent
        @close-auto-focus="restoreFocus"
        @escape-key-down="busy && $event.preventDefault()"
        @interact-outside="busy && $event.preventDefault()"
      >
        <AlertDialogHeader>
          <AlertDialogTitle>{{
            t("components.attributeDefinitions.deleteTitle", {
              count: deleteIds.length,
            })
          }}</AlertDialogTitle>
          <AlertDialogDescription>{{
            t("components.attributeDefinitions.deleteDescription")
          }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter layout="split">
          <AlertDialogCancel as-child>
            <Button variant="outline" size="lg" :disabled="busy">{{
              t("common.cancel")
            }}</Button>
          </AlertDialogCancel>
          <Button
            variant="destructive"
            size="lg"
            :disabled="busy"
            @click="runAction('delete', [...deleteIds])"
          >
            <Spinner v-if="busy" class="mr-2" />{{ t("common.delete") }}
          </Button>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
