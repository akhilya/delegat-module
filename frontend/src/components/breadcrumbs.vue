<script setup>
import { useRouter } from "vue-router";
import { ChevronRight, Home } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";

defineProps({
  items: {
    type: Array,
    required: true,
    // Each item: { label: string, to?: string }
  },
});

const router = useRouter();

function navigate(to) {
  if (to) router.push(to);
}
</script>

<template>
  <Breadcrumb class="mb-4">
    <BreadcrumbList>
      <BreadcrumbItem>
        <BreadcrumbLink as-child>
          <Button
            variant="ghost"
            size="icon"
            class="size-7"
            aria-label="Главная"
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
              class="h-auto px-0 text-muted-foreground font-normal"
              @click="navigate(item.to)"
            >
              {{ item.label }}
            </Button>
          </BreadcrumbLink>
          <BreadcrumbPage v-else>
            {{ item.label }}
          </BreadcrumbPage>
        </BreadcrumbItem>
      </template>
    </BreadcrumbList>
  </Breadcrumb>
</template>