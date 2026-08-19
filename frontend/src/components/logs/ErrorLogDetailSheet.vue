<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { Check, Copy, Trash2 } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import { InsetGroupLabel } from "@/components/ui/inset-group";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import LogDetailSheet from "./LogDetailSheet.vue";
import LogMetaGrid from "./LogMetaGrid.vue";
import LogCodeBlock from "./LogCodeBlock.vue";

const props = defineProps({
  open: { type: Boolean, default: false },
  log: { type: Object, default: null },
  metaItems: { type: Array, default: () => [] },
  copied: { type: Boolean, default: false },
  deleting: { type: Boolean, default: false },
});

const emit = defineEmits(["update:open", "copy", "remove"]);

const { t } = useI18n();

function formatPath(log) {
  if (!log) return "";
  return `${log.path || ""}${log.queryString || ""}`;
}

const title = computed(() => {
  if (!props.log) return t("pages.errorLogsPage.detailFallbackTitle");
  return `${props.log.method} · ${props.log.statusCode}`;
});

const description = computed(() =>
  props.log ? formatPath(props.log) : "…",
);

function onCopy() {
  if (props.log) emit("copy", props.log);
}

function onRemove() {
  if (props.log) emit("remove", props.log);
}
</script>

<template>
  <LogDetailSheet
    :open="open"
    :title="title"
    :description="description"
    :aria-label="t('pages.errorLogsPage.detailFallbackTitle')"
    @update:open="emit('update:open', $event)"
  >
    <template v-if="log" #actions>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        class="h-8"
        @click="onCopy"
      >
        <Check v-if="copied" class="size-3.5" />
        <Copy v-else class="size-3.5" />
        {{ t("pages.errorLogsPage.copyAll") }}
      </Button>
      <AlertDialog>
        <AlertDialogTrigger as-child>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            class="relative z-10 size-8 text-destructive hover:bg-destructive/10 hover:text-destructive"
            :disabled="deleting"
            :title="t('common.delete')"
            :aria-label="t('common.delete')"
          >
            <Spinner v-if="deleting" size="sm" />
            <Trash2 v-else class="size-4" />
          </Button>
        </AlertDialogTrigger>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {{ t("pages.errorLogsPage.deleteConfirmTitle") }}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {{ t("pages.errorLogsPage.deleteConfirmDescription") }}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>{{ t("common.cancel") }}</AlertDialogCancel>
            <AlertDialogAction variant="destructive" @click="onRemove">
              {{ t("common.delete") }}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </template>

    <div
      v-if="log"
      class="min-h-0 flex-1 space-y-5 overflow-y-auto px-4 py-4"
    >
      <div>
        <InsetGroupLabel>{{ t("common.details") }}</InsetGroupLabel>
        <LogMetaGrid :items="metaItems" />
      </div>

      <LogCodeBlock
        :label="t('pages.errorLogsPage.messageLabel')"
        :text="log.message || '-'"
        tone="destructive"
        max-height-class="max-h-28"
      />
      <LogCodeBlock
        :label="t('pages.errorLogsPage.requestLabel')"
        :text="formatPath(log)"
        max-height-class="max-h-24"
      />
      <LogCodeBlock
        :label="t('pages.errorLogsPage.traceIdLabel')"
        :text="log.traceIdentifier || '-'"
        max-height-class="max-h-20"
      />
      <LogCodeBlock
        :label="t('pages.errorLogsPage.stackTraceLabel')"
        :text="log.stackTrace || '-'"
        max-height-class="max-h-96"
      />
    </div>
  </LogDetailSheet>
</template>
