<script setup>
import { computed, ref, watch } from "vue";
import { useI18n } from "vue-i18n";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import Input from "@/components/ui/input/Input.vue";
import { Button } from "@/components/ui/button";
import {
  localizeExternalFieldName,
  localizeStandardSourceName,
} from "./fieldLabels.js";

const { t } = useI18n();

const props = defineProps({
  open: { type: Boolean, default: false },
  field: { type: Object, default: null },
  availableSources: { type: Object, default: () => ({}) },
  adapterFields: { type: Array, default: () => [] },
  currentValue: { type: String, default: "" },
});

const emit = defineEmits(["update:open", "select"]);

const search = ref("");

watch(
  () => props.open,
  (o) => {
    if (o) search.value = "";
  },
);

const q = computed(() => search.value.trim().toLowerCase());
const match = (label, key) => {
  if (!q.value) return true;
  const hay = `${label || ""} ${key || ""}`.toLowerCase();
  return hay.includes(q.value);
};

const avail = computed(() => props.availableSources || {});

const productFields = computed(() =>
  (avail.value.productFields || [])
    .map((pf) => ({
      ...pf,
      displayName: localizeStandardSourceName(pf.key, pf.displayName),
    }))
    .filter((pf) => match(pf.displayName, pf.key)),
);
const stockFields = computed(() =>
  (avail.value.stockFields || [])
    .map((pf) => ({
      ...pf,
      displayName: localizeStandardSourceName(pf.key, pf.displayName),
    }))
    .filter((pf) => match(pf.displayName, pf.key)),
);
const priceFields = computed(() =>
  (avail.value.priceFields || [])
    .map((pf) => ({
      ...pf,
      displayName: localizeStandardSourceName(pf.key, pf.displayName),
    }))
    .filter((pf) => match(pf.displayName, pf.key)),
);
const categoryAttributes = computed(() =>
  (avail.value.categoryAttributes || []).filter((a) => match(a.name, a.id)),
);
const storeAttributes = computed(() =>
  (avail.value.storeAttributes || []).filter((a) => match(a.name, a.id)),
);
const showGroupName = computed(() =>
  match(t("marketplace.graph.inspector.groupNameOption"), "groupName"),
);

const mergeCandidates = computed(() =>
  (props.adapterFields || []).filter(
    (x) =>
      x.fieldKey !== props.field?.fieldKey &&
      !x.aliasOf &&
      !x.omit &&
      match(x.label, x.fieldKey),
  ),
);

const fieldTitle = computed(() =>
  localizeExternalFieldName(
    props.field?.fieldKey,
    props.field?.label || props.field?.fieldKey || "",
  ),
);

function pick(value) {
  emit("select", value);
  emit("update:open", false);
}

function isActive(value) {
  return props.currentValue === value;
}
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent
      size="lg"
      class="flex max-h-[min(80vh,640px)] flex-col gap-0 overflow-hidden p-0"
    >
      <DialogHeader variant="bar">
        <DialogTitle>
          {{
            t("marketplace.graph.inspector.bindingDialogTitle", {
              field: fieldTitle,
            })
          }}
        </DialogTitle>
        <DialogDescription>
          {{ t("marketplace.graph.inspector.bindingDialogHint") }}
        </DialogDescription>
      </DialogHeader>

      <div class="shrink-0 border-b border-border px-6 py-3">
        <Input
          v-model="search"
          type="search"
          :placeholder="
            t('marketplace.graph.inspector.bindingSearchPlaceholder')
          "
          class="h-9"
        />
      </div>

      <div class="min-h-0 flex-1 overflow-y-auto px-4 py-3">
        <section v-if="productFields.length" class="mb-3">
          <h4
            class="px-2 pb-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
          >
            {{ t("marketplace.graph.inspector.productGroup") }}
          </h4>
          <Button
            v-for="pf in productFields"
            :key="'p-' + pf.key"
            variant="ghost"
            type="button"
            class="h-auto min-h-0 w-full justify-start rounded-md px-2 py-1.5 text-left text-sm font-normal whitespace-normal hover:bg-accent"
            :class="isActive(`p:${pf.key}`) && 'bg-accent font-medium'"
            @click="pick(`p:${pf.key}`)"
          >
            {{ pf.displayName || pf.key }}
          </Button>
        </section>

        <section v-if="stockFields.length" class="mb-3">
          <h4
            class="px-2 pb-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
          >
            {{ t("marketplace.graph.inspector.stockGroup") }}
          </h4>
          <Button
            v-for="pf in stockFields"
            :key="'stock-' + pf.key"
            variant="ghost"
            type="button"
            class="h-auto min-h-0 w-full justify-start rounded-md px-2 py-1.5 text-left text-sm font-normal whitespace-normal hover:bg-accent"
            :class="isActive(`p:${pf.key}`) && 'bg-accent font-medium'"
            @click="pick(`p:${pf.key}`)"
          >
            {{ pf.displayName || pf.key }}
          </Button>
        </section>

        <section v-if="priceFields.length" class="mb-3">
          <h4
            class="px-2 pb-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
          >
            {{ t("marketplace.graph.inspector.priceGroup") }}
          </h4>
          <Button
            v-for="pf in priceFields"
            :key="'price-' + pf.key"
            variant="ghost"
            type="button"
            class="h-auto min-h-0 w-full justify-start rounded-md px-2 py-1.5 text-left text-sm font-normal whitespace-normal hover:bg-accent"
            :class="isActive(`p:${pf.key}`) && 'bg-accent font-medium'"
            @click="pick(`p:${pf.key}`)"
          >
            {{ pf.displayName || pf.key }}
          </Button>
        </section>

        <section v-if="categoryAttributes.length" class="mb-3">
          <h4
            class="px-2 pb-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
          >
            {{ t("marketplace.graph.inspector.categoryAttributeGroup") }}
          </h4>
          <Button
            v-for="a in categoryAttributes"
            :key="'a-' + a.id"
            variant="ghost"
            type="button"
            class="h-auto min-h-0 w-full justify-start rounded-md px-2 py-1.5 text-left text-sm font-normal whitespace-normal hover:bg-accent"
            :class="isActive(`a:${a.id}`) && 'bg-accent font-medium'"
            @click="pick(`a:${a.id}`)"
          >
            {{ a.name }}
          </Button>
        </section>

        <section v-if="storeAttributes.length" class="mb-3">
          <h4
            class="px-2 pb-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
          >
            {{ t("marketplace.graph.inspector.storeAttributeGroup") }}
          </h4>
          <Button
            v-for="a in storeAttributes"
            :key="'s-' + a.id"
            variant="ghost"
            type="button"
            class="h-auto min-h-0 w-full justify-start rounded-md px-2 py-1.5 text-left text-sm font-normal whitespace-normal hover:bg-accent"
            :class="isActive(`s:${a.id}`) && 'bg-accent font-medium'"
            @click="pick(`s:${a.id}`)"
          >
            {{ a.name }}
          </Button>
        </section>

        <section v-if="showGroupName" class="mb-3">
          <h4
            class="px-2 pb-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
          >
            {{ t("marketplace.graph.inspector.otherSourcesGroup") }}
          </h4>
          <Button
            variant="ghost"
            type="button"
            class="h-auto min-h-0 w-full justify-start rounded-md px-2 py-1.5 text-left text-sm font-normal whitespace-normal hover:bg-accent"
            :class="isActive('g') && 'bg-accent font-medium'"
            @click="pick('g')"
          >
            {{ t("marketplace.graph.inspector.groupNameOption") }}
          </Button>
        </section>

        <section
          v-if="mergeCandidates.length"
          class="mb-2 rounded-md border border-dashed bg-muted/30 p-2"
        >
          <h4
            class="px-1 pb-0.5 text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
          >
            {{ t("marketplace.graph.inspector.aliasSectionTitle") }}
          </h4>
          <p class="mb-2 px-1 text-[11px] leading-snug text-muted-foreground">
            {{ t("marketplace.graph.inspector.aliasSectionHint") }}
          </p>
          <Button
            v-for="mf in mergeCandidates"
            :key="'m-' + mf.fieldKey"
            variant="ghost"
            type="button"
            class="h-auto min-h-0 w-full justify-start rounded-md px-2 py-1.5 text-left text-sm font-normal whitespace-normal hover:bg-accent"
            :class="isActive(`m:${mf.fieldKey}`) && 'bg-accent font-medium'"
            @click="pick(`m:${mf.fieldKey}`)"
          >
            =
            {{
              localizeExternalFieldName(mf.fieldKey, mf.label || mf.fieldKey)
            }}
          </Button>
        </section>

        <p
          v-if="
            !productFields.length &&
            !stockFields.length &&
            !priceFields.length &&
            !categoryAttributes.length &&
            !storeAttributes.length &&
            !showGroupName &&
            !mergeCandidates.length
          "
          class="px-2 py-6 text-center text-sm text-muted-foreground"
        >
          {{ t("marketplace.graph.inspector.bindingEmpty") }}
        </p>
      </div>
    </DialogContent>
  </Dialog>
</template>
