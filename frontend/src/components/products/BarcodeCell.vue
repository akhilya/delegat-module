<script>
import JsBarcode from "jsbarcode";
import { ref, onMounted, nextTick, watch } from "vue";
import { Button } from "@/components/ui/button";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { Download } from "lucide-vue-next";
import { downloadBarcodePng } from "@/lib/barcodeDownload.js";

function getLineColor() {
  const raw = getComputedStyle(document.documentElement)
    .getPropertyValue("--foreground")
    .trim();
  return raw || "#000000";
}

export default {
  components: { Popover, PopoverContent, PopoverTrigger, Button, Download },
  props: {
    barcode: { type: String, required: true },
  },
  setup(props) {
    const smallSvg = ref(null);
    const largeSvg = ref(null);
    const isOpen = ref(false);

    const format = props.barcode.length === 8 ? "EAN8" : "EAN13";

    function renderSmall() {
      if (!smallSvg.value) return;
      try {
        JsBarcode(smallSvg.value, props.barcode, {
          format,
          height: 32,
          displayValue: false,
          margin: 0,
          width: 1.2,
          lineColor: getLineColor(),
        });
      } catch {}
    }

    function renderLarge() {
      if (!largeSvg.value) return;
      try {
        JsBarcode(largeSvg.value, props.barcode, {
          format,
          height: 80,
          displayValue: true,
          margin: 8,
          fontSize: 16,
          lineColor: getLineColor(),
        });
      } catch {}
    }

    function downloadBarcode() {
      downloadBarcodePng(props.barcode, format);
    }

    onMounted(() => {
      nextTick(renderSmall);
    });

    watch(
      () => props.barcode,
      async () => {
        await nextTick();
        renderSmall();
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
      <button
        type="button"
        class="flex flex-col items-center gap-0.5 rounded-md px-1 py-0.5 cursor-pointer transition-colors hover:bg-muted/60"
        :title="$t('barcodeCell.clickToEnlarge')"
      >
        <svg ref="smallSvg" class="block h-8 w-[96px] max-w-full"></svg>
        <span class="text-[10px] font-mono leading-none tabular-nums text-foreground/80">
          {{ barcode }}
        </span>
      </button>
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