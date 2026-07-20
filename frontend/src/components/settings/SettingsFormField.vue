<script setup>
import { computed, useSlots } from "vue";
import { CircleHelp } from "lucide-vue-next";
import { t } from "@/i18n";
import { cn } from "@/lib/utils";
import {
  Field,
  FieldLabel,
  FieldDescription,
  FieldError,
  FieldContent,
} from "@/components/ui/field";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";

const props = defineProps({
  label: { type: String, required: true },
  labelFor: { type: String, default: undefined },
  description: { type: String, default: "" },
  descriptionInTooltip: { type: Boolean, default: false },
  error: { type: String, default: "" },
  required: { type: Boolean, default: false },
  inline: { type: Boolean, default: false },
  class: { type: null, default: undefined },
});

const slots = useSlots();
const hasError = computed(() => !!props.error);
const hasDescription = computed(
  () => !!(props.description || slots.description),
);
const showInlineDescription = computed(
  () => hasDescription.value && !props.descriptionInTooltip,
);
const showDescriptionTooltip = computed(
  () => hasDescription.value && props.descriptionInTooltip,
);
</script>

<template>
  <TooltipProvider :delay-duration="200">
    <Field
      :class="cn('min-w-0 gap-1.5', props.class)"
      :data-invalid="hasError || undefined"
    >
      <template v-if="inline">
        <div class="flex items-center gap-3 h-9">
          <slot />
          <div class="flex min-w-0 items-center gap-1.5">
            <FieldLabel
              :for="labelFor"
              :class="cn(
                'text-sm font-medium leading-none cursor-pointer',
                hasError && 'text-destructive',
              )"
            >
              {{ label }}<span v-if="required" class="text-destructive"> *</span>
            </FieldLabel>
            <Tooltip v-if="showDescriptionTooltip">
              <TooltipTrigger as-child>
                <button
                  type="button"
                  class="inline-flex shrink-0 text-muted-foreground transition-colors hover:text-foreground"
                  :aria-label="t('components.settingsFormField.descriptionAriaLabel', { label })"
                >
                  <CircleHelp class="size-3.5" />
                </button>
              </TooltipTrigger>
              <TooltipContent
                side="top"
                class="max-w-sm whitespace-pre-wrap text-balance"
              >
                <slot name="description">{{ description }}</slot>
              </TooltipContent>
            </Tooltip>
          </div>
        </div>
        <FieldDescription
          v-if="showInlineDescription"
          :class="cn(
            'text-xs pl-[calc(2.25rem+0.75rem)]',
            hasError ? 'text-destructive' : 'text-muted-foreground',
          )"
        >
          <slot name="description">{{ description }}</slot>
        </FieldDescription>
      </template>

      <template v-else>
        <div class="flex min-w-0 items-start gap-1.5">
          <FieldLabel
            :for="labelFor"
            :title="label"
            :class="cn('min-w-0 max-w-full line-clamp-2 break-all text-sm font-medium', hasError && 'text-destructive')"
          >
            {{ label }}<span v-if="required" class="text-destructive"> *</span>
          </FieldLabel>
          <Tooltip v-if="showDescriptionTooltip">
            <TooltipTrigger as-child>
              <button
                type="button"
                class="mt-0.5 inline-flex shrink-0 text-muted-foreground transition-colors hover:text-foreground"
                :aria-label="t('components.settingsFormField.descriptionAriaLabel', { label })"
              >
                <CircleHelp class="size-3.5" />
              </button>
            </TooltipTrigger>
            <TooltipContent
              side="top"
              class="max-w-sm whitespace-pre-wrap text-balance"
            >
              <slot name="description">{{ description }}</slot>
            </TooltipContent>
          </Tooltip>
        </div>
        <FieldContent class="min-w-0 w-full overflow-hidden">
          <slot />
        </FieldContent>
        <FieldDescription
          v-if="showInlineDescription"
          :class="cn('text-xs', hasError ? 'text-destructive' : 'text-muted-foreground')"
        >
          <slot name="description">{{ description }}</slot>
        </FieldDescription>
      </template>

      <FieldError v-if="error">{{ error }}</FieldError>
    </Field>
  </TooltipProvider>
</template>