<script setup>
/**
 * Catalog / order-line identity: rounded thumb + two truncated lines.
 * Thumb size follows the shared table photo-size preference.
 */
import ProductImageCell from "@/components/products/ProductImageCell.vue";
import { useProductImageSize } from "@/composables/useProductImageSize";
import { useI18n } from "vue-i18n";
import { RouterLink } from "vue-router";

defineProps({
  name: { type: String, default: "" },
  caption: { type: String, default: "" },
  thumbnailUrl: { type: String, default: null },
  imageUrl: { type: String, default: null },
  showImage: { type: Boolean, default: true },
  to: { type: [String, Object], default: null },
});

const { t } = useI18n();
const { spec } = useProductImageSize();
</script>

<template>
  <div
    data-slot="product-identity-cell"
    class="flex min-w-0 items-center gap-3"
    :class="spec.identityClass"
  >
    <ProductImageCell
      v-if="showImage"
      :image-class="spec.thumbClass"
      :thumbnail-url="thumbnailUrl"
      :image-url="imageUrl"
    />
    <div
      class="flex min-w-0 flex-1 flex-col justify-center gap-1"
      :class="spec.identityClass"
    >
      <p class="flex min-w-0 items-baseline gap-1.5 leading-none">
        <RouterLink
          v-if="to"
          :to="to"
          class="truncate text-sm leading-none font-medium tracking-tight hover:underline"
          @click.stop
          >{{ name || t("common.emDash") }}</RouterLink
        >
        <span
          v-else
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
