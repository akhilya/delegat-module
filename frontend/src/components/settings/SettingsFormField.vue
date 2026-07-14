<script setup>
import { computed } from "vue";
import { cn } from "@/lib/utils";
import {
  Field,
  FieldLabel,
  FieldDescription,
  FieldError,
  FieldContent,
} from "@/components/ui/field";

const props = defineProps({
  label: { type: String, required: true },
  labelFor: { type: String, default: undefined },
  description: { type: String, default: "" },
  error: { type: String, default: "" },
  required: { type: Boolean, default: false },
  inline: { type: Boolean, default: false },
  class: { type: null, default: undefined },
});

const hasError = computed(() => !!props.error);
</script>

<template>
  <Field
    :class="cn('min-w-0 gap-1.5', props.class)"
    :data-invalid="hasError || undefined"
  >
    <template v-if="inline">
      <div class="flex items-center gap-3 h-9">
        <slot />
        <FieldLabel
          :for="labelFor"
          :class="cn(
            'text-sm font-medium leading-none cursor-pointer',
            hasError && 'text-destructive',
          )"
        >
          {{ label }}<span v-if="required" class="text-destructive"> *</span>
        </FieldLabel>
      </div>
      <FieldDescription
        v-if="description || $slots.description"
        :class="cn(
          'text-xs pl-[calc(2.25rem+0.75rem)]',
          hasError ? 'text-destructive' : 'text-muted-foreground',
        )"
      >
        <slot name="description">{{ description }}</slot>
      </FieldDescription>
    </template>

    <template v-else>
      <FieldLabel
        :for="labelFor"
        :title="label"
        :class="cn('min-w-0 max-w-full line-clamp-2 break-all text-sm font-medium', hasError && 'text-destructive')"
      >
        {{ label }}<span v-if="required" class="text-destructive"> *</span>
      </FieldLabel>
      <FieldContent class="min-w-0 w-full overflow-hidden">
        <slot />
      </FieldContent>
      <FieldDescription
        v-if="description || $slots.description"
        :class="cn('text-xs', hasError ? 'text-destructive' : 'text-muted-foreground')"
      >
        <slot name="description">{{ description }}</slot>
      </FieldDescription>
    </template>

    <FieldError v-if="error">{{ error }}</FieldError>
  </Field>
</template>