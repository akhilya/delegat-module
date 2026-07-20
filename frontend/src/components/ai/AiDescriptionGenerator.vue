<script setup>
import { ref, computed, onMounted } from "vue";
import { useStore } from "vuex";
import { http } from "@/main";
import { Sparkles } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { displaySonnerError } from "@/store/sonnerHelper";
import { useI18n } from "vue-i18n";

const { t } = useI18n();

const props = defineProps({
  productName: { type: String, default: "" },
  productSku: { type: String, default: "" },
  categoryName: { type: String, default: "" },
  existingDescription: { type: String, default: "" },
  attributesSummary: { type: String, default: "" },
  modelId: { type: Number, default: null },
});

const emit = defineEmits(["generated", "update:generating"]);

const vuexStore = useStore();
const isLoading = ref(false);

const hasModels = computed(() => (vuexStore.state.aiModels?.pickerModels ?? []).length > 0);
const hasExistingDescription = computed(() => Boolean(props.existingDescription?.trim()));
const buttonLabel = computed(() => {
  if (isLoading.value) {
    return hasExistingDescription.value
      ? t("ai.improvingWithAi")
      : t("ai.generatingWithAi");
  }
  return hasExistingDescription.value
    ? t("ai.improveWithAi")
    : t("ai.generateWithAi");
});

onMounted(() => {
  vuexStore.dispatch("aiModels/getPickerModels", { onError: () => {} });
});

async function generate() {
  if (!props.productName?.trim()) {
    displaySonnerError(t("ai.provideProductNameFirst"));
    return;
  }
  isLoading.value = true;
  emit("update:generating", true);
  try {
    const payload = {
      name: props.productName.trim(),
      sku: props.productSku || null,
      categoryName: props.categoryName || null,
      existingDescription: props.existingDescription?.trim() || null,
      attributesSummary: props.attributesSummary || null,
    };
    if (props.modelId != null) payload.modelId = props.modelId;

    const r = await http.post("/ai/generate-product-description", payload, { timeout: 120000 });
    const text = r.data?.description;
    if (text) emit("generated", text);
    else displaySonnerError(t("ai.emptyAiResponse"));
  } catch (e) {
    displaySonnerError(e);
  } finally {
    isLoading.value = false;
    emit("update:generating", false);
  }
}
</script>

<template>
  <div v-if="hasModels" class="flex items-center gap-2">
    <Button
      type="button"
      variant="ghost"
      size="sm"
      class="h-7 text-xs gap-1.5 px-2"
      :disabled="isLoading"
      :aria-busy="isLoading"
      @click="generate"
    >
      <Sparkles class="size-3" :class="isLoading ? 'opacity-40' : ''" />
      {{ buttonLabel }}
    </Button>
  </div>
</template>
