<script setup>
import { computed } from "vue";
import { Download, FileText, Files, X } from "lucide-vue-next";
import { toast } from "vue-sonner";
import { useI18n } from "vue-i18n";
import { Button } from "@/components/ui/button";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";
import { attachmentPreview } from "@/lib/ai/attachments";
import { fetchWithAuth } from "@/utils/fetchWithAuth";

const props = defineProps({
  files: { type: Array, default: () => [] },
  clearable: { type: Boolean, default: false },
});

const emit = defineEmits(["clear"]);
const { t } = useI18n();

const preview = computed(() => attachmentPreview(props.files));
const showTtlHint = computed(
  () => !props.clearable && props.files.some((f) => f?.id),
);

async function downloadFile(file) {
  if (!file?.id || props.clearable) return;
  try {
    const res = await fetchWithAuth(`/api/ai/chat/attachments/${file.id}`);
    if (res.status === 404) {
      toast.error(t("ai.attachmentExpired"));
      return;
    }
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = file.fileName || "file";
    a.click();
    URL.revokeObjectURL(url);
  } catch {
    toast.error(t("ai.attachmentDownloadFailed"));
  }
}
</script>

<template>
  <InsetGroup
    v-if="preview.total"
    class="w-full"
    :class="clearable ? '' : 'max-w-[80%]'"
  >
    <InsetGroupItem v-if="clearable" as="div">
      <template #leading>
        <Files class="text-muted-foreground" />
      </template>
      <p class="truncate leading-snug">
        {{ t("ai.attachedFiles", { n: preview.total }) }}
      </p>
      <template #trailing>
        <Button
          variant="ghost"
          size="icon"
          class="size-8"
          :title="t('ai.clearAttachments')"
          :aria-label="t('ai.clearAttachments')"
          @click="emit('clear')"
        >
          <X class="size-4" />
        </Button>
      </template>
    </InsetGroupItem>
    <InsetGroupItem
      v-for="file in preview.shown"
      :key="file.relativePath"
      as="div"
      class="py-1.5"
    >
      <template #leading>
        <FileText class="text-muted-foreground" />
      </template>
      <p class="truncate leading-snug">{{ file.fileName }}</p>
      <p
        v-if="file.relativePath && file.relativePath !== file.fileName"
        class="truncate text-[11px] text-muted-foreground"
      >
        {{ file.relativePath }}
      </p>
      <template v-if="!clearable && file.id" #trailing>
        <Button
          variant="ghost"
          size="icon"
          class="size-8"
          :title="t('ai.downloadAttachment')"
          :aria-label="t('ai.downloadAttachment')"
          @click="downloadFile(file)"
        >
          <Download class="size-4" />
        </Button>
      </template>
    </InsetGroupItem>
    <InsetGroupItem v-if="preview.hidden" as="div" class="py-1.5">
      <p class="text-xs text-muted-foreground">
        {{ t("ai.moreAttachments", { n: preview.hidden }) }}
      </p>
    </InsetGroupItem>
    <InsetGroupItem v-if="showTtlHint" as="div" class="py-1.5">
      <p class="text-[11px] text-muted-foreground">
        {{ t("ai.attachmentTtlHint") }}
      </p>
    </InsetGroupItem>
  </InsetGroup>
</template>
