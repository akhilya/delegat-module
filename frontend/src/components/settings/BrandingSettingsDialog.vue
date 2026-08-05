<script setup>
import { computed, ref, watch, nextTick } from "vue";
import { useI18n } from "vue-i18n";
import { Loader2, Palette } from "lucide-vue-next";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import BrandingForm from "@/components/settings/BrandingForm.vue";

const props = defineProps({
  open: { type: Boolean, default: false },
});

const emit = defineEmits(["update:open"]);

const { t } = useI18n();
const formRef = ref(null);

const openProxy = computed({
  get: () => props.open,
  set: (v) => emit("update:open", v),
});

const saving = computed(() => formRef.value?.saving ?? false);
const loading = computed(() => formRef.value?.loading ?? false);
const dirty = computed(() => formRef.value?.dirty ?? false);
const canSave = computed(() => formRef.value?.canSave ?? false);

watch(
  () => props.open,
  async (isOpen) => {
    if (!isOpen) return;
    await nextTick();
    formRef.value?.load?.();
  },
);

async function save() {
  const data = await formRef.value?.save?.();
  if (data) openProxy.value = false;
}

function onOpenChange(v) {
  openProxy.value = v;
}
</script>

<template>
  <Dialog :open="openProxy" @update:open="onOpenChange">
    <DialogContent
      size="2xl"
      class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
    >
      <DialogHeader class="shrink-0 space-y-3 border-b px-6 py-5 text-left">
        <div class="flex items-start gap-3 pr-8">
          <div class="flex size-10 shrink-0 items-center justify-center rounded-lg border bg-muted/40">
            <Palette class="size-5 text-muted-foreground" />
          </div>
          <div class="min-w-0 flex-1 space-y-1">
            <DialogTitle>
              {{ t("pages.settingsOverviewPage.branding.dialogTitle") }}
            </DialogTitle>
            <DialogDescription>
              {{ t("pages.settingsOverviewPage.branding.dialogDescription") }}
            </DialogDescription>
          </div>
        </div>
      </DialogHeader>

      <div class="flex-1 overflow-y-auto px-6 py-5">
        <BrandingForm
          ref="formRef"
          :load-on-mount="false"
        />
      </div>

      <DialogFooter class="shrink-0 border-t bg-muted/20 px-6 py-4">
        <DialogClose as-child>
          <Button type="button" variant="outline" :disabled="saving">
            {{ t("common.cancel") }}
          </Button>
        </DialogClose>
        <Button
          type="button"
          :disabled="loading || saving || !dirty || !canSave"
          @click="save"
        >
          <Loader2 v-if="saving" class="mr-2 size-4 animate-spin" />
          {{ t("common.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
