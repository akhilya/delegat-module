<script setup>
/**
 * Small / medium / large product thumbs. Ghost-icon dropdown (catalog scrape).
 */
import { SlidersHorizontal } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuTrigger,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
} from "@/components/ui/dropdown-menu";
import { useProductImageSize } from "@/composables/useProductImageSize";
import { PRODUCT_IMAGE_SIZE_IDS } from "@/lib/productImageSize";

defineProps({
  /** Radios only — already inside the products column dropdown. */
  embedded: { type: Boolean, default: false },
});

const { sizeId, setSize } = useProductImageSize();

const SIZE_LABEL_KEYS = {
  sm: "columnSelector.photoSizeSm",
  md: "columnSelector.photoSizeMd",
  lg: "columnSelector.photoSizeLg",
};
</script>

<template>
  <template v-if="embedded">
    <DropdownMenuLabel>{{ $t("columnSelector.photoSize") }}</DropdownMenuLabel>
    <DropdownMenuSeparator />
    <DropdownMenuRadioGroup :model-value="sizeId" @update:model-value="setSize">
      <DropdownMenuRadioItem
        v-for="id in PRODUCT_IMAGE_SIZE_IDS"
        :key="id"
        :value="id"
      >
        {{ $t(SIZE_LABEL_KEYS[id]) }}
      </DropdownMenuRadioItem>
    </DropdownMenuRadioGroup>
  </template>
  <DropdownMenu v-else>
    <DropdownMenuTrigger as-child>
      <Button
        variant="ghost"
        size="icon"
        class="size-8"
        :title="$t('columnSelector.photoSize')"
      >
        <SlidersHorizontal class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-56">
      <DropdownMenuLabel>{{
        $t("columnSelector.photoSize")
      }}</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuRadioGroup
        :model-value="sizeId"
        @update:model-value="setSize"
      >
        <DropdownMenuRadioItem
          v-for="id in PRODUCT_IMAGE_SIZE_IDS"
          :key="id"
          :value="id"
        >
          {{ $t(SIZE_LABEL_KEYS[id]) }}
        </DropdownMenuRadioItem>
      </DropdownMenuRadioGroup>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
