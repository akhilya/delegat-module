<script setup>
/**
 * Packing overview: one Apple list cell (thumb, name, id, amount).
 * Full line items stay on the order sheet. Thumb size follows the shared
 * catalog / mailbox photo-size preference.
 */
import ProductImageCell from "@/components/products/ProductImageCell.vue";
import { InsetGroupItem } from "@/components/ui/inset-group";
import { useProductImageSize } from "@/composables/useProductImageSize";
import {
  orderAssemblyMoreCount,
  orderAssemblyTitle,
  orderDisplayId,
  orderItemImageUrls,
} from "@/lib/ordersUi";
import { cn } from "@/lib/utils";
import { computed } from "vue";
import { useI18n } from "vue-i18n";

defineOptions({ inheritAttrs: false });

const props = defineProps({
  row: { type: Object, required: true },
  money: { type: String, default: "" },
  status: { type: String, default: "" },
});

const { t } = useI18n();
const { spec } = useProductImageSize();

const title = computed(() => orderAssemblyTitle(props.row));
const displayId = computed(() => orderDisplayId(props.row));
const moreCount = computed(() => orderAssemblyMoreCount(props.row));
const thumbs = computed(() => orderItemImageUrls(props.row, 3));
const caption = computed(() => {
  if (moreCount.value > 0) {
    return t("pages.ordersPage.assemblyRowMore", {
      id: displayId.value,
      n: moreCount.value,
    });
  }
  return displayId.value;
});
const stackWidth = computed(() => {
  const n = Math.max(thumbs.value.length, 1);
  return `${spec.value.px + (n - 1) * spec.value.stackPeek}px`;
});
const stackedThumbClass = computed(() =>
  cn(spec.value.imageOnlyClass, "ring-2 ring-card"),
);
</script>

<template>
  <InsetGroupItem v-bind="$attrs" class="items-center py-2">
    <div class="flex min-w-0 items-center gap-3">
      <div
        class="relative isolate shrink-0"
        :class="spec.identityClass"
        :style="{ width: stackWidth }"
        aria-hidden="true"
      >
        <ProductImageCell
          v-if="thumbs.length === 0"
          :image-class="spec.imageOnlyClass"
          :stop-events="false"
        />
        <div
          v-for="(url, index) in thumbs"
          :key="`${index}-${url}`"
          class="absolute top-0"
          :style="{
            left: `${index * spec.stackPeek}px`,
            zIndex: thumbs.length - index,
          }"
        >
          <ProductImageCell
            :image-class="stackedThumbClass"
            :thumbnail-url="url"
            :image-url="url"
            :stop-events="false"
          />
        </div>
      </div>
      <div
        class="flex min-w-0 flex-1 flex-col justify-center gap-1"
        :class="spec.identityClass"
      >
        <p class="truncate text-sm leading-none font-medium tracking-tight">
          {{ title }}
        </p>
        <p
          class="min-w-0 truncate text-xs leading-none text-muted-foreground tabular-nums"
        >
          {{ caption }}
        </p>
      </div>
    </div>
    <template #trailing>
      <div class="flex min-w-16 flex-col items-end justify-center gap-1">
        <span
          class="text-sm leading-none font-medium tabular-nums tracking-tight"
        >
          {{ money || t("common.emDash") }}
        </span>
        <span class="text-xs leading-none text-muted-foreground">
          {{ status }}
        </span>
      </div>
    </template>
  </InsetGroupItem>
</template>
