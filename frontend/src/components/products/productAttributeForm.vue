<script>
import { ref, watch, nextTick } from "vue";
import { useFormErrors, useFormValues, useSubmitCount } from "vee-validate";
import { FieldGroup } from "@/components/ui/field";
import { EmptyState } from "@/components/ui/empty-state";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import {
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Rows3, Columns3 } from "lucide-vue-next";
import {
  ATTRIBUTE_GROUP_OPTIONS,
  attributeGroupKeyForValue,
} from "@/constants/attributeGroups";
import {
  isAttributeType,
  isEmptyAttributeValue,
} from "@/lib/productAttributeValidation";
import { alignAttributeRows } from "@/lib/attributeRowAlign";
import AttributeFieldControl from "@/components/products/AttributeFieldControl.vue";
import AttributeGroupIcon from "@/components/products/AttributeGroupIcon.vue";

const OTHER_GROUP = "\u041f\u0440\u043e\u0447\u0435\u0435";
const LAYOUT_STORAGE_KEY = "delegat.productAttributeLayout";

function readStoredLayout() {
  try {
    return localStorage.getItem(LAYOUT_STORAGE_KEY) === "compare"
      ? "compare"
      : "groups";
  } catch {
    return "groups";
  }
}

function collectInvalidIds(errors, prefix) {
  const ids = new Set();
  if (!errors || typeof errors !== "object") return ids;

  const nested = errors[prefix];
  if (nested && typeof nested === "object" && !Array.isArray(nested)) {
    for (const [key, value] of Object.entries(nested)) {
      if (value) ids.add(String(key));
    }
  }

  for (const [key, value] of Object.entries(errors)) {
    if (!value || typeof value === "object") continue;

    if (key.startsWith(`${prefix}[`)) {
      const end = key.indexOf("]");
      if (end > prefix.length + 1) {
        ids.add(key.slice(prefix.length + 1, end));
      }
      continue;
    }

    if (key.startsWith(`${prefix}.`)) {
      ids.add(key.slice(prefix.length + 1).split(/[.[]/)[0]);
    }
  }

  return ids;
}

function groupIconFromAttributes(attrs) {
  return attrs?.find((attr) => attr.groupIcon)?.groupIcon || "";
}

function collectEmptyRequiredIds(attributes, valuesById) {
  const ids = new Set();
  const values = valuesById || {};
  for (const attr of attributes || []) {
    if (attr.showOnProductPage === false) continue;
    if (!attr.isRequired) continue;
    const id = String(attr.id);
    const raw = values[id] ?? values[attr.id];
    if (isEmptyAttributeValue(attr, raw)) ids.add(id);
  }
  return ids;
}

export default {
  components: {
    EmptyState,
    FieldGroup,
    Tabs,
    TabsList,
    TabsTrigger,
    Accordion,
    AccordionContent,
    AccordionItem,
    AccordionTrigger,
    CardAction,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
    Rows3,
    Columns3,
    AttributeFieldControl,
    AttributeGroupIcon,
  },
  props: {
    attributes: {
      type: Array,
      required: true,
    },
    fieldPrefix: {
      type: String,
      default: "categoryAttributes",
    },
  },
  setup(props) {
    const formErrors = useFormErrors();
    const formValues = useFormValues();
    const submitCount = useSubmitCount();
    const flaggedAttributeIds = ref([]);

    function recomputeFlaggedIds() {
      const next = collectInvalidIds(formErrors.value, props.fieldPrefix);

      if (submitCount.value > 0) {
        const bucket = formValues.value?.[props.fieldPrefix];
        for (const id of collectEmptyRequiredIds(props.attributes, bucket)) {
          next.add(id);
        }
      }

      const alive = new Set(
        (props.attributes || [])
          .filter((a) => a.showOnProductPage !== false)
          .map((a) => String(a.id)),
      );
      flaggedAttributeIds.value = [...next].filter((id) => alive.has(id));
    }

    watch(formErrors, recomputeFlaggedIds, { deep: true, immediate: true });
    watch(formValues, recomputeFlaggedIds, { deep: true });
    watch(submitCount, async () => {
      await nextTick();
      recomputeFlaggedIds();
    });
    watch(() => props.attributes, recomputeFlaggedIds);

    return {
      flaggedAttributeIds,
    };
  },
  data() {
    return {
      groupOrder: ATTRIBUTE_GROUP_OPTIONS.map((option) => option.value),
      layout: readStoredLayout(),
    };
  },
  computed: {
    sortedAttributes() {
      return [...this.attributes]
        .filter((attr) => attr.showOnProductPage !== false)
        .sort((a, b) => a.sortOrder - b.sortOrder);
    },
    groupedAttributes() {
      const groups = {};

      for (const attr of this.sortedAttributes) {
        const groupName = attr.group || OTHER_GROUP;
        if (!groups[groupName]) {
          groups[groupName] = [];
        }
        groups[groupName].push(attr);
      }

      const result = [];

      for (const groupName of this.groupOrder) {
        if (groupName === OTHER_GROUP) continue;
        if (groups[groupName]) {
          result.push({
            name: groupName,
            label: this.groupLabel(groupName),
            icon: groupIconFromAttributes(groups[groupName]),
            attributes: groups[groupName],
          });
          delete groups[groupName];
        }
      }

      for (const [groupName, attrs] of Object.entries(groups)) {
        if (groupName === OTHER_GROUP) continue;
        result.push({
          name: groupName,
          label: this.groupLabel(groupName),
          icon: groupIconFromAttributes(attrs),
          attributes: attrs,
        });
      }

      if (groups[OTHER_GROUP]) {
        result.push({
          name: OTHER_GROUP,
          label: this.groupLabel(OTHER_GROUP),
          icon: groupIconFromAttributes(groups[OTHER_GROUP]),
          attributes: groups[OTHER_GROUP],
        });
      }

      return result;
    },
    showGroupHeaders() {
      return (
        this.groupedAttributes.length > 1 ||
        (this.groupedAttributes.length === 1 &&
          this.groupedAttributes[0].name !== OTHER_GROUP)
      );
    },
    showLayoutToggle() {
      return this.groupedAttributes.length > 1;
    },
    showCompareLayout() {
      return this.showLayoutToggle && this.layout === "compare";
    },
    alignedRows() {
      return alignAttributeRows(this.groupedAttributes);
    },
    compareGridStyle() {
      const n = Math.max(this.groupedAttributes.length, 1);
      const gapRem = 1.25;
      return {
        gridTemplateColumns: `repeat(${n}, minmax(16rem, 1fr))`,
        minWidth: `max(100%, calc(${n} * 16rem + ${n - 1} * ${gapRem}rem))`,
      };
    },
    defaultOpenGroups() {
      return this.groupedAttributes.map((group) => group.name);
    },
    groupErrorState() {
      const flagged = new Set(this.flaggedAttributeIds);
      const state = {};
      for (const group of this.groupedAttributes) {
        state[group.name] = group.attributes.some((attr) =>
          flagged.has(String(attr.id)),
        );
      }
      return state;
    },
  },
  methods: {
    groupLabel(groupName) {
      const key = attributeGroupKeyForValue(groupName);
      return key ? this.$t(`attributeGroups.${key}`) : groupName;
    },

    isFullWidthField(attribute) {
      return isAttributeType(attribute.type, "text") && attribute.isMultiline;
    },

    setLayout(next) {
      this.layout = next === "compare" ? "compare" : "groups";
      try {
        localStorage.setItem(LAYOUT_STORAGE_KEY, this.layout);
      } catch {
        /* ignore quota / private mode */
      }
    },
  },
};
</script>

<template>
  <div class="contents">
    <CardHeader v-if="$slots.title || $slots.description || showLayoutToggle">
      <CardTitle v-if="$slots.title" class="text-base">
        <slot name="title" />
      </CardTitle>
      <CardDescription v-if="$slots.description">
        <slot name="description" />
      </CardDescription>
      <CardAction v-if="showLayoutToggle">
        <Tabs
          :model-value="layout"
          class="gap-0"
          :aria-label="$t('products.productAttributeForm.layoutLabel')"
          @update:model-value="setLayout"
        >
          <TabsList>
            <TabsTrigger value="groups" class="gap-1.5 px-2.5">
              <Rows3 class="size-4" />
              {{ $t("products.productAttributeForm.layoutGroups") }}
            </TabsTrigger>
            <TabsTrigger value="compare" class="gap-1.5 px-2.5">
              <Columns3 class="size-4" />
              {{ $t("products.productAttributeForm.layoutCompare") }}
            </TabsTrigger>
          </TabsList>
        </Tabs>
      </CardAction>
    </CardHeader>

    <CardContent class="pt-0">
      <div class="min-w-0 space-y-8">
        <div
          v-if="showCompareLayout"
          class="relative max-h-[min(70vh,52rem)] min-w-0 overflow-auto overscroll-contain"
        >
          <div
            class="sticky top-0 z-10 border-b border-border/70 bg-card pb-3 pt-0.5"
            :style="{ minWidth: compareGridStyle.minWidth }"
          >
            <div class="grid gap-x-5" :style="compareGridStyle">
              <div
                v-for="group in groupedAttributes"
                :key="'h-' + group.name"
                class="flex min-w-0 items-center gap-2 text-sm font-medium"
                :class="
                  groupErrorState[group.name]
                    ? 'text-destructive'
                    : 'text-muted-foreground'
                "
              >
                <AttributeGroupIcon
                  class="size-4"
                  :group-name="group.name"
                  :group-icon="group.icon"
                />
                <span class="min-w-0 truncate">{{ group.label }}</span>
              </div>
            </div>
          </div>
          <div class="grid gap-x-5 gap-y-5 pt-5" :style="compareGridStyle">
            <template
              v-for="(row, rowIndex) in alignedRows"
              :key="'r-' + rowIndex"
            >
              <div
                v-for="(attribute, colIndex) in row"
                :key="'c-' + rowIndex + '-' + colIndex"
                class="min-w-0 overflow-hidden"
              >
                <AttributeFieldControl
                  v-if="attribute"
                  :attribute="attribute"
                  :field-prefix="fieldPrefix"
                />
                <p v-else class="py-2 text-sm text-muted-foreground">
                  {{ $t("products.productAttributeForm.emptyCell") }}
                </p>
              </div>
            </template>
          </div>
        </div>

        <Accordion
          v-else-if="showGroupHeaders"
          type="multiple"
          :default-value="defaultOpenGroups"
        >
          <AccordionItem
            v-for="group in groupedAttributes"
            :key="group.name"
            :value="group.name"
            :data-invalid="groupErrorState[group.name] ? 'true' : undefined"
            :class="
              groupErrorState[group.name] ? 'border-destructive' : undefined
            "
          >
            <AccordionTrigger
              class="py-3"
              :class="
                groupErrorState[group.name]
                  ? 'text-destructive [&_svg]:text-destructive'
                  : 'text-muted-foreground'
              "
            >
              <span
                class="flex min-w-0 items-center gap-2"
                :class="
                  groupErrorState[group.name] ? 'text-destructive' : undefined
                "
                :style="
                  groupErrorState[group.name]
                    ? { color: 'var(--destructive)' }
                    : undefined
                "
              >
                <AttributeGroupIcon
                  class="size-4"
                  :group-name="group.name"
                  :group-icon="group.icon"
                />
                <span class="min-w-0 truncate">{{ group.label }}</span>
              </span>
            </AccordionTrigger>
            <AccordionContent>
              <FieldGroup class="gap-5">
                <div
                  class="grid min-w-0 grid-cols-1 gap-5 md:grid-cols-[repeat(2,minmax(0,1fr))]"
                >
                  <div
                    v-for="attribute in group.attributes"
                    :key="attribute.id"
                    class="min-w-0 overflow-hidden"
                    :class="isFullWidthField(attribute) ? 'md:col-span-2' : ''"
                  >
                    <AttributeFieldControl
                      :attribute="attribute"
                      :field-prefix="fieldPrefix"
                    />
                  </div>
                </div>
              </FieldGroup>
            </AccordionContent>
          </AccordionItem>
        </Accordion>

        <template v-else>
          <div
            v-for="group in groupedAttributes"
            :key="group.name"
            class="space-y-5"
          >
            <FieldGroup class="gap-5">
              <div
                class="grid min-w-0 grid-cols-1 gap-5 md:grid-cols-[repeat(2,minmax(0,1fr))]"
              >
                <div
                  v-for="attribute in group.attributes"
                  :key="attribute.id"
                  class="min-w-0 overflow-hidden"
                  :class="isFullWidthField(attribute) ? 'md:col-span-2' : ''"
                >
                  <AttributeFieldControl
                    :attribute="attribute"
                    :field-prefix="fieldPrefix"
                  />
                </div>
              </div>
            </FieldGroup>
          </div>
        </template>

        <EmptyState
          v-if="sortedAttributes.length === 0"
          size="sm"
          :title="$t('products.productAttributeForm.emptyTitle')"
          :description="$t('products.productAttributeForm.emptyDescription')"
        />
      </div>
    </CardContent>
  </div>
</template>
