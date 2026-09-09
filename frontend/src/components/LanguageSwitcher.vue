<script setup>
import { computed } from "vue";
import { useI18n } from "vue-i18n";
import { setLocale, SUPPORTED_LOCALES } from "@/i18n";
import LocaleFlag from "@/components/LocaleFlag.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

const props = defineProps({
  /** compact = sidebar; full = labeled; corner = auth chrome */
  variant: { type: String, default: "compact" },
});

const { t, locale } = useI18n();

const current = computed({
  get: () => locale.value,
  set: (value) => setLocale(value),
});

const options = SUPPORTED_LOCALES.map((code) => ({
  code,
  labelKey: `language.${code}`,
}));

const isCorner = computed(() => props.variant === "corner");
</script>

<template>
  <div
    class="flex items-center gap-2"
    :class="isCorner ? 'w-auto' : 'w-full'"
  >
    <span
      v-if="variant === 'full'"
      class="text-muted-foreground shrink-0 text-sm"
    >
      {{ t("language.label") }}
    </span>
    <Select v-model="current">
      <SelectTrigger
        class="h-9"
        :class="isCorner
          ? 'w-auto min-w-[7.5rem] border-border/70 bg-card shadow-none'
          : 'w-full'"
        :aria-label="t('language.label')"
      >
        <SelectValue>
          <span class="flex min-w-0 items-center gap-2">
            <LocaleFlag :code="current" />
            <span class="truncate">{{ t(`language.${current}`) }}</span>
          </span>
        </SelectValue>
      </SelectTrigger>
      <SelectContent>
        <SelectItem
          v-for="opt in options"
          :key="opt.code"
          :value="opt.code"
          :text-value="t(opt.labelKey)"
        >
          <span class="flex items-center gap-2">
            <LocaleFlag :code="opt.code" />
            <span>{{ t(opt.labelKey) }}</span>
          </span>
        </SelectItem>
      </SelectContent>
    </Select>
  </div>
</template>
