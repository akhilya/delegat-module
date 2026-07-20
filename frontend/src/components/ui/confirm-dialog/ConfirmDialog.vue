<script setup>
import { Loader2 } from "lucide-vue-next";
import { t } from "@/i18n";
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

const props = defineProps({
  title: { type: String, required: true },
  description: { type: String, default: '' },
  confirmLabel: { type: String, default: () => t('common.delete') },
  cancelLabel: { type: String, default: () => t('common.cancel') },
  loading: { type: Boolean, default: false },
  destructive: { type: Boolean, default: true },
})

const emit = defineEmits(['confirm'])
</script>

<template>
  <AlertDialog>
    <AlertDialogTrigger as-child>
      <slot name="trigger" />
    </AlertDialogTrigger>
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>{{ title }}</AlertDialogTitle>
        <AlertDialogDescription v-if="description">
          {{ description }}
        </AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>{{ cancelLabel }}</AlertDialogCancel>
        <AlertDialogAction
          :class="destructive ? 'bg-destructive text-white hover:bg-destructive/90' : ''"
          :disabled="loading"
          @click="emit('confirm')"
        >
          <Loader2 v-if="loading" class="size-4 mr-2 animate-spin" />
          {{ confirmLabel }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
