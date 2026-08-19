<script setup>
import { X } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { t } from "@/i18n";
import DialogClose from "./DialogClose.vue";
</script>

<template>
  <DialogClose as-child>
    <Button
      type="button"
      variant="ghost"
      size="icon"
      class="size-8 shrink-0 text-muted-foreground"
      :aria-label="t('common.close')"
    >
      <X class="size-4" />
    </Button>
  </DialogClose>
</template>
