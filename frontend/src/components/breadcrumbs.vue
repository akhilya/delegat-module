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
  class: { type: null, required: false },
});

const router = useRouter();

function navigate(to) {
  if (to) router.push(to);
}
</script>

<template>
  <!-- Fixed row height (home size-7): link crumbs must not use default Button py-2/h-9 or the trail grows and looks «shifted». -->
  <Breadcrumb :class="cn('mb-4', props.class)">
    <BreadcrumbList class="min-h-7">
      <BreadcrumbItem>
        <BreadcrumbLink as-child>
          <Button
            variant="ghost"
            size="icon"
            class="-ml-1.5 size-7"
            :aria-label="t('components.breadcrumbs.homeAriaLabel')"
            @click="navigate('/')"
          >
            <Home class="size-4" />
          </Button>
        </BreadcrumbLink>
      </BreadcrumbItem>
      <template v-for="(item, index) in items" :key="index">
        <BreadcrumbSeparator>
          <ChevronRight class="size-4" />
        </BreadcrumbSeparator>
        <BreadcrumbItem>
          <BreadcrumbLink v-if="item.to && index < items.length - 1" as-child>
            <Button
              variant="link"
              class="h-7 px-0 py-0 text-sm font-normal text-muted-foreground"
              @click="navigate(item.to)"
            >
              {{ item.label }}
            </Button>
          </BreadcrumbLink>
          <BreadcrumbPage v-else class="inline-flex h-7 items-center text-sm">
            {{ item.label }}
          </BreadcrumbPage>
        </BreadcrumbItem>
      </template>
    </BreadcrumbList>
  </Breadcrumb>
</template>