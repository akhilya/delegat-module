<script setup>
import { ref, computed, onMounted } from "vue";
import { useStore } from "vuex";
import { http } from "@/main";
import { Loader2, Sparkles } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { displaySonnerError } from "@/store/sonnerHelper";

const props = defineProps({
  productName: { type: String, default: "" },
  productSku: { type: String, default: "" },
  categoryName: { type: String, default: "" },
  existingDescription: { type: String, default: "" },
  attributesSummary: { type: String, default: "" },
  modelId: { type: Number, default: null },
});

const emit = defineEmits(["generated"]);

const vuexStore = useStore();
const isLoading = ref(false);

const hasModels = computed(() => (vuexStore.state.aiModels?.pickerModels ?? []).length > 0);

onMounted(() => {
  vuexStore.dispatch("aiModels/getPickerModels", { onError: () => {} });
});

async function generate() {
  if (!props.productName?.trim()) {
    displaySonnerError("Сначала укажите название товара");
    return;
  }
  isLoading.value = true;
  try {
    const payload = {
      name: props.productName.trim(),
      sku: props.productSku || null,
      categoryName: props.categoryName || null,
      existingDescription: props.existingDescription || null,
      attributesSummary: props.attributesSummary || null,
    };
    if (props.modelId != null) payload.modelId = props.modelId;

    const r = await http.post("/ai/generate-product-description", payload, { timeout: 120000 });
    const text = r.data?.description;
    if (text) emit("generated", text);
    else displaySonnerError("Пустой ответ от AI");
  } catch (e) {
    displaySonnerError(e);
  } finally {
    isLoading.value = false;
  }
}
</script>

<template>
  <Button
    v-if="hasModels"
    type="button"
    variant="ghost"
    size="sm"
    class="h-7 text-xs gap-1.5 px-2"
    :disabled="isLoading"
    @click="generate"
  >
    <Loader2 v-if="isLoading" class="size-3 animate-spin" />
    <Sparkles v-else class="size-3" />
    Сгенерировать с ИИ
  </Button>
</template>