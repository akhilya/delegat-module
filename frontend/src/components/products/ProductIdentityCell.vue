<script setup>
/**
 * Catalog / order-line identity: 44px rounded thumb + two truncated lines.
 * Fixed height so every products-table row matches.
 */
import ProductImageCell from "@/components/products/ProductImageCell.vue";
import { useI18n } from "vue-i18n";

defineProps({
  name: { type: String, default: "" },
  caption: { type: String, default: "" },
  thumbnailUrl: { type: String, default: null },
  imageUrl: { type: String, default: null },
  showImage: { type: Boolean, default: true },
});

const { t } = useI18n();
</script>

<template>
  <div data-slot="product-identity-cell" class="flex h-11 min-w-0 items-center gap-3">
    <ProductImageCell
      v-if="showImage"
      image-class="size-11 shrink-0 rounded-lg shadow-sm ring-2 ring-background"
      :thumbnail-url="thumbnailUrl"
      :image-url="imageUrl"
    />
    <div class="flex h-11 min-w-0 flex-1 flex-col justify-center gap-1">
      <p class="flex min-w-0 items-baseline gap-1.5 leading-none">
        <span
          class="truncate text-sm leading-none font-medium tracking-tight"
          >{{ name || t("common.emDash") }}</span
        >
        <span v-if="$slots.meta" class="min-w-0 shrink-0">
          <slot name="meta" />
        </span>
      </p>
      <p
        class="min-w-0 truncate text-xs leading-none text-muted-foreground"
        :class="caption || $slots.caption ? '' : 'invisible'"
      >
        <slot name="caption">{{ caption || t("common.emDash") }}</slot>
      </p>
    </div>
  </div>
</template>
