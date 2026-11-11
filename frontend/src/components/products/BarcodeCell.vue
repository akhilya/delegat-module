<script>
import JsBarcode from "jsbarcode";
import { computed, ref, onMounted, nextTick, watch } from "vue";
import { Button } from "@/components/ui/button";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { Download } from "lucide-vue-next";

const SMALL_BARCODE_HEIGHT = 32;

export default {
  components: { Popover, PopoverContent, PopoverTrigger, Button, Download },
  props: {
    barcode: { type: String, required: true },
  },
  setup(props) {
    const smallSvg = ref(null);
    const largeSvg = ref(null);
    const isOpen = ref(false);

    const format = computed(() =>
      props.barcode.length === 8 ? "EAN8" : "EAN13",
    );

    function renderSmall() {
      if (!smallSvg.value) return;
      try {
        JsBarcode(smallSvg.value, props.barcode, {
          format: format.value,
          height: SMALL_BARCODE_HEIGHT,
          displayValue: false,
          margin: 0,
          width: 1.8,
          lineColor: "#111111",
          background: "#ffffff",
        });
      } catch {
        // Keep the preview empty when the barcode value is invalid.
      }
    }

    function renderLarge() {
      if (!largeSvg.value) return;
      try {
        JsBarcode(largeSvg.value, props.barcode, {
          format: format.value,
          height: 80,
          displayValue: true,
          margin: 8,
          fontSize: 16,
          lineColor: "#111111",
          background: "#ffffff",
        });
      } catch {
        // Keep the preview empty when the barcode value is invalid.
      }
    }

    async function downloadBarcode() {
      const { downloadBarcodePng } = await import("@/lib/barcodeDownload.js");
      await downloadBarcodePng(props.barcode, format.value);
    }

    onMounted(() => {
      nextTick(renderSmall);
    });

    watch(
      () => props.barcode,
      async () => {
        await nextTick();
        renderSmall();
        if (isOpen.value) renderLarge();
      },
    );

    watch(isOpen, async (val) => {
      if (val) {
        await nextTick();
        renderLarge();
      }
    });

    return { smallSvg, largeSvg, isOpen, downloadBarcode };
  },
};
</script>

<template>
  <Popover v-model:open="isOpen">
    <PopoverTrigger as-child>
      <Button
        type="button"
        variant="ghost"
        class="h-auto min-h-0 w-full flex-col gap-1 rounded-md px-1 py-0.5 font-normal hover:bg-muted/60"
        :aria-label="$t('barcodeCell.clickToEnlarge')"
        :title="$t('barcodeCell.clickToEnlarge')"
      >
        <span
          class="flex w-full flex-col items-center gap-1 rounded-sm bg-white px-1 py-0.5 text-neutral-950 shadow-[inset_0_0_0_1px_rgb(0_0_0/0.10)]"
        >
          <div class="h-8 w-full">
            <svg ref="smallSvg" class="block size-full"></svg>
          </div>
          <span class="text-xs leading-none font-semibold tabular-nums">
            {{ barcode }}
          </span>
        </span>
      </Button>
    </PopoverTrigger>
    <PopoverContent class="!max-w-none !w-max p-2" align="center">
      <div class="flex flex-col items-stretch gap-2">
        <svg ref="largeSvg" class="block"></svg>
        <Button
          type="button"
          variant="outline"
          size="sm"
          class="w-full shrink-0"
          @click="downloadBarcode"
        >
          <Download class="size-3.5 mr-1.5" />
          {{ $t("barcodeCell.downloadPng") }}
        </Button>
      </div>
    </PopoverContent>
  </Popover>
</template>
