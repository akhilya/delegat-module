<script setup>
/**
 * Photo size radios for a row context menu (catalog, trash, orders).
 */
import { Image } from "lucide-vue-next";
import {
  ContextMenuRadioGroup,
  ContextMenuRadioItem,
  ContextMenuSub,
  ContextMenuSubContent,
  ContextMenuSubTrigger,
} from "@/components/ui/context-menu";
import { useProductImageSize } from "@/composables/useProductImageSize";
import { PRODUCT_IMAGE_SIZE_IDS } from "@/lib/productImageSize";

const SIZE_LABEL_KEYS = {
  sm: "columnSelector.photoSizeSm",
  md: "columnSelector.photoSizeMd",
  lg: "columnSelector.photoSizeLg",
};

const { sizeId, setSize } = useProductImageSize();
</script>

<template>
  <ContextMenuSub>
    <ContextMenuSubTrigger>
      <Image />
      {{ $t("columnSelector.photoSize") }}
    </ContextMenuSubTrigger>
    <ContextMenuSubContent class="w-44">
      <ContextMenuRadioGroup :model-value="sizeId" @update:model-value="setSize">
        <ContextMenuRadioItem
          v-for="id in PRODUCT_IMAGE_SIZE_IDS"
          :key="id"
          :value="id"
        >
          {{ $t(SIZE_LABEL_KEYS[id]) }}
        </ContextMenuRadioItem>
      </ContextMenuRadioGroup>
    </ContextMenuSubContent>
  </ContextMenuSub>
</template>
