<script setup>
import { useRouter } from "vue-router";
import { ChevronRight, Home } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { t } from "@/i18n";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";

const props = defineProps({
  items: {
    type: Array,
    required: true,
    // Each item: { label: string, to?: string }
  },
  /** Compact trail for the page navbar (single row, no wrap, no page margin). */
  inset: {
    type: Boolean,
    default: false,
  },
  class: { type: null, required: false },
});

const router = useRouter();

function navigate(to) {
  if (to) router.push(to);
}
</script>

<template>
  <!-- Fixed row height (home size-7): link crumbs must not use default Button py-2/h-9 or the trail grows and looks «shifted». -->
  <Breadcrumb :class="cn(inset ? 'min-w-0' : 'mb-4', props.class)">
    <BreadcrumbList
      :class="cn('min-h-7', inset && 'flex-nowrap overflow-hidden')"
    >
      <BreadcrumbItem class="shrink-0 max-lg:hidden">
        <BreadcrumbLink as-child>
          <Button
            variant="ghost"
            size="icon"
            class="size-7"
            :aria-label="t('components.breadcrumbs.homeAriaLabel')"
            @click="navigate('/')"
          >
            <Home class="size-4" />
          </Button>
        </BreadcrumbLink>
      </BreadcrumbItem>
      <template v-for="(item, index) in items" :key="index">
        <BreadcrumbSeparator class="shrink-0 max-lg:hidden">
          <ChevronRight class="size-4" />
        </BreadcrumbSeparator>
        <BreadcrumbItem
          :class="
            index < items.length - 1 ? 'min-w-0 max-lg:hidden' : 'min-w-0'
          "
        >
          <BreadcrumbLink v-if="item.to && index < items.length - 1" as-child>
            <Button
              variant="link"
              class="h-7 max-w-full truncate px-0 py-0 text-sm font-normal text-muted-foreground"
              @click="navigate(item.to)"
            >
              {{ item.label }}
            </Button>
          </BreadcrumbLink>
          <BreadcrumbPage
            v-else
            class="inline-flex h-7 max-w-full min-w-0 items-center truncate text-sm max-lg:font-medium"
            :title="item.label"
          >
            {{ item.label }}
          </BreadcrumbPage>
        </BreadcrumbItem>
      </template>
    </BreadcrumbList>
  </Breadcrumb>
</template>
