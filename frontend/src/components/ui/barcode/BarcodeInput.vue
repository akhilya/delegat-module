<script>
import Input from "@/components/ui/input/Input.vue";
import { Button } from "@/components/ui/button";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover";
import { Dices, Download } from "lucide-vue-next";
import JsBarcode from "jsbarcode";
import { ref, computed, watch, nextTick } from "vue";
import { t } from "@/i18n";

export default {
  components: {
    Input, Button, Dices, Download,
    Popover, PopoverContent, PopoverTrigger,
    AlertDialog, AlertDialogAction, AlertDialogCancel,
    AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
    AlertDialogHeader, AlertDialogTitle,
  },
  props: {
    modelValue: {
      type: String,
      default: "",
    },
    showPreview: {
      type: Boolean,
      default: true,
    },
  },
  emits: ["update:modelValue"],
  setup(props, { emit }) {
    const svgRef = ref(null);
    const largeSvgRef = ref(null);
    const popoverOpen = ref(false);
    const showConfirm = ref(false);

    function calcCheckDigit(digits) {
      let sum = 0;
      for (let i = 0; i < digits.length; i++) {
        const d = parseInt(digits[i], 10);
        sum += i % 2 === 0 ? d : d * 3;
      }
      return (10 - (sum % 10)) % 10;
    }

    function isValidEan(code) {
      if (!code) return false;
      if (code.length !== 8 && code.length !== 13) return false;
      if (!/^\d+$/.test(code)) return false;
      const body = code.slice(0, -1);
      const check = parseInt(code.slice(-1), 10);
      return calcCheckDigit(body) === check;
    }

    const isValid = computed(() => {
      if (!props.modelValue) return true; // empty is ok
      return isValidEan(props.modelValue);
    });

    const errorMessage = computed(() => {
      if (!props.modelValue) return null;
      if (!/^\d*$/.test(props.modelValue)) return t("components.barcodeInput.onlyDigitsError");
      if (props.modelValue.length > 13) return t("components.barcodeInput.maxDigitsError");
      if (props.modelValue.length !== 8 && props.modelValue.length !== 13)
        return t("components.barcodeInput.digitsCountError");
      if (!isValidEan(props.modelValue)) return t("components.barcodeInput.invalidCheckDigitError");
      return null;
    });

    const barcodeFormat = computed(() => {
      if (!props.modelValue) return null;
      if (props.modelValue.length === 8) return "EAN8";
      if (props.modelValue.length === 13) return "EAN13";
      return null;
    });

    function renderBarcode() {
      if (!svgRef.value || !isValid.value || !barcodeFormat.value) return;
      try {
        JsBarcode(svgRef.value, props.modelValue, {
          format: barcodeFormat.value,
          height: 24,
          displayValue: false,
          margin: 0,
          width: 1,
        });
      } catch {}
    }

    function renderLargeBarcode() {
      if (!largeSvgRef.value || !isValid.value || !barcodeFormat.value) return;
      try {
        JsBarcode(largeSvgRef.value, props.modelValue, {
          format: barcodeFormat.value,
          height: 80,
          displayValue: true,
          margin: 8,
          fontSize: 16,
        });
      } catch {}
    }

    // Re-render when value changes
    watch(
      () => props.modelValue,
      async () => {
        await nextTick();
        renderBarcode();
      }
    );

    // Render when SVG element first appears in DOM
    watch(svgRef, (el) => {
      if (el) renderBarcode();
    });

    // Render large barcode when popover opens
    watch(popoverOpen, async (val) => {
      if (val) {
        await nextTick();
        renderLargeBarcode();
      }
    });

    watch(largeSvgRef, (el) => {
      if (el) renderLargeBarcode();
    });

    function onInput(value) {
      // Only allow digits, max 13
      const cleaned = value.replace(/\D/g, "").slice(0, 13);
      emit("update:modelValue", cleaned);

      // Auto-complete check digit if 7 or 12 digits
      if (cleaned.length === 7 || cleaned.length === 12) {
        const check = calcCheckDigit(cleaned);
        emit("update:modelValue", cleaned + check);
      }
    }

    function doGenerate() {
      const prefix = "20";
      let body = prefix;
      for (let i = 0; i < 10; i++) {
        body += Math.floor(Math.random() * 10).toString();
      }
      const check = calcCheckDigit(body);
      emit("update:modelValue", body + check);
    }

    function generate() {
      // If barcode already exists, ask for confirmation
      if (props.modelValue && isValidEan(props.modelValue)) {
        showConfirm.value = true;
        return;
      }
      doGenerate();
    }

    function confirmGenerate() {
      showConfirm.value = false;
      doGenerate();
    }

    async function downloadBarcode() {
      if (!isValid.value || !barcodeFormat.value) return;
      const { downloadBarcodePng } = await import("@/lib/barcodeDownload.js");
      await downloadBarcodePng(props.modelValue, barcodeFormat.value);
    }

    return {
      svgRef,
      largeSvgRef,
      popoverOpen,
      isValid,
      errorMessage,
      barcodeFormat,
      showConfirm,
      onInput,
      generate,
      confirmGenerate,
      downloadBarcode,
      t,
    };
  },
};
</script>

<template>
  <div class="w-full min-w-0">
    <div class="flex min-w-0 items-center gap-1.5">
      <Input
        class="min-w-0 flex-1"
        type="text"
        inputmode="numeric"
        pattern="\d*"
        maxlength="13"
        :model-value="modelValue"
        @update:model-value="onInput"
        placeholder="EAN-8 / EAN-13"
        :aria-label="t('components.barcodeInput.ariaLabel')"
        :class="{ 'border-destructive focus-visible:ring-destructive': modelValue && !isValid }"
      />
      <Button type="button" variant="outline" size="icon" class="shrink-0 size-9" :title="t('components.barcodeInput.generateTitle')" @click="generate">
        <Dices class="size-4" />
      </Button>

      <!-- Inline barcode preview - click to expand -->
      <Popover v-if="showPreview && modelValue && isValid && barcodeFormat" v-model:open="popoverOpen">
        <PopoverTrigger as-child>
          <button
            type="button"
            class="shrink-0 h-9 bg-background border rounded px-1.5 flex items-center cursor-pointer hover:border-foreground/30 transition-colors"
            :title="t('barcodeCell.clickToEnlarge')"
          >
            <svg ref="svgRef" class="h-6 block"></svg>
          </button>
        </PopoverTrigger>
        <PopoverContent class="!max-w-none !w-max p-2" align="end">
          <div class="flex flex-col items-stretch gap-2">
            <svg ref="largeSvgRef" class="block"></svg>
            <Button
              type="button"
              variant="outline"
              size="sm"
              class="w-full shrink-0"
              @click="downloadBarcode"
            >
              <Download class="size-3.5 mr-1.5" />
              {{ t('barcodeCell.downloadPng') }}
            </Button>
          </div>
        </PopoverContent>
      </Popover>
    </div>

    <p v-if="errorMessage" class="text-xs text-destructive mt-1">
      {{ errorMessage }}
    </p>

    <!-- Confirmation dialog for regeneration -->
    <AlertDialog :open="showConfirm" @update:open="showConfirm = $event">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ t('components.barcodeInput.regenerateTitle') }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ t('components.barcodeInput.regenerateDescription', { value: modelValue }) }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ t('common.cancel') }}</AlertDialogCancel>
          <AlertDialogAction @click="confirmGenerate">{{ t('components.barcodeInput.replace') }}</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </div>
</template>
