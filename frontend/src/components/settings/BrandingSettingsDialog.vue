<script setup>
import { Spinner } from "@/components/ui/spinner";
import { computed, ref, watch, nextTick } from "vue";
import { useI18n } from "vue-i18n";
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
      <DialogHeader variant="bar">
        <DialogTitle>
          {{ t("pages.settingsOverviewPage.branding.dialogTitle") }}
        </DialogTitle>
        <DialogDescription>
          {{ t("pages.settingsOverviewPage.branding.dialogDescription") }}
        </DialogDescription>
      </DialogHeader>

      <div class="flex-1 overflow-y-auto px-6 py-5">
        <BrandingForm
          ref="formRef"
          :load-on-mount="false"
        />
      </div>

      <DialogFooter variant="bar">
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
          <Spinner v-if="saving" class="mr-2" />
          {{ t("common.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
