<script>
import JsBarcode from "jsbarcode";
import { ref, onMounted, nextTick, watch } from "vue";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";

export default {
  components: { Popover, PopoverContent, PopoverTrigger },
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
          height: 28,
          displayValue: false,
          margin: 0,
          width: 1,
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
        });
      } catch {}
    }

    onMounted(() => {
      nextTick(renderSmall);
    });

    watch(isOpen, async (val) => {
      if (val) {
        await nextTick();
        renderLarge();
      }
    });

    return { smallSvg, largeSvg, isOpen, renderSmall, renderLarge };
  },
};
</script>

<template>
  <Popover v-model:open="isOpen">
    <PopoverTrigger as-child>
      <button
        type="button"
        class="flex flex-col items-center gap-0.5 cursor-pointer hover:opacity-80 transition-opacity"
        title="Нажмите для увеличения"
      >
        <svg ref="smallSvg" class="h-7"></svg>
        <span class="text-[10px] text-muted-foreground font-mono leading-none">{{ barcode }}</span>
      </button>
    </PopoverTrigger>
    <PopoverContent class="w-auto p-2" align="center">
      <div class="bg-background rounded p-1">
        <svg ref="largeSvg"></svg>
      </div>
    </PopoverContent>
  </Popover>
</template>
