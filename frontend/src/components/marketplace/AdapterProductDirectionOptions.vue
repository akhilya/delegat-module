<script setup>
import {
  ArrowDownToLine,
  ArrowUpToLine,
  ChevronRight,
  Link2,
} from "@/lib/icons";
import { Button } from "@/components/ui/button";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";

defineProps({
  adapterName: { type: String, default: "" },
  canImport: { type: Boolean, default: true },
  canPublish: { type: Boolean, default: true },
  canSync: { type: Boolean, default: false },
});

const emit = defineEmits(["import", "publish", "sync"]);
</script>

<template>
  <InsetGroup class="my-2">
    <InsetGroupItem as="div" class="min-h-16" :disabled="!canImport">
      <template #leading>
        <span
          class="flex size-9 items-center justify-center rounded-full bg-muted text-foreground"
        >
          <ArrowDownToLine class="size-4" />
        </span>
      </template>
      <div class="min-w-0">
        <p class="font-medium">
          {{
            $t("marketplace.connectionPage.products.setupImportTitle", {
              adapter: adapterName,
            })
          }}
        </p>
        <p
          class="mt-0.5 text-xs font-normal leading-relaxed text-muted-foreground"
        >
          {{
            $t("marketplace.connectionPage.products.setupImportDescription", {
              adapter: adapterName,
            })
          }}
        </p>
        <p
          v-if="!canImport"
          class="mt-1 text-xs font-medium text-warning-foreground"
        >
          {{ $t("marketplace.connectionPage.products.setupUnsupported") }}
        </p>
      </div>
      <template #trailing>
        <Button
          size="sm"
          variant="outline"
          :disabled="!canImport"
          @click="emit('import')"
        >
          {{ $t("marketplace.connectionPage.products.setupChoose") }}
          <ChevronRight class="ml-1 size-4" />
        </Button>
      </template>
    </InsetGroupItem>
    <InsetGroupItem v-if="canSync" as="div" class="min-h-16">
      <template #leading>
        <span
          class="flex size-9 items-center justify-center rounded-full bg-muted text-foreground"
        >
          <Link2 class="size-4" />
        </span>
      </template>
      <div class="min-w-0">
        <p class="font-medium">
          {{ $t("marketplace.marketplacePage.syncCatalogTitle") }}
        </p>
        <p
          class="mt-0.5 text-xs font-normal leading-relaxed text-muted-foreground"
        >
          {{ $t("marketplace.marketplacePage.syncCatalogDescription") }}
        </p>
      </div>
      <template #trailing>
        <Button
          size="sm"
          variant="outline"
          :disabled="!canSync"
          @click="emit('sync')"
        >
          {{ $t("marketplace.connectionPage.products.setupChoose") }}
          <ChevronRight class="ml-1 size-4" />
        </Button>
      </template>
    </InsetGroupItem>
    <InsetGroupItem as="div" class="min-h-16" :disabled="!canPublish">
      <template #leading>
        <span
          class="flex size-9 items-center justify-center rounded-full bg-muted text-foreground"
        >
          <ArrowUpToLine class="size-4" />
        </span>
      </template>
      <div class="min-w-0">
        <p class="font-medium">
          {{
            $t("marketplace.connectionPage.products.setupPublishTitle", {
              adapter: adapterName,
            })
          }}
        </p>
        <p
          class="mt-0.5 text-xs font-normal leading-relaxed text-muted-foreground"
        >
          {{
            $t("marketplace.connectionPage.products.setupPublishDescription", {
              adapter: adapterName,
            })
          }}
        </p>
        <p
          v-if="!canPublish"
          class="mt-1 text-xs font-medium text-warning-foreground"
        >
          {{ $t("marketplace.connectionPage.products.setupUnsupported") }}
        </p>
      </div>
      <template #trailing>
        <Button
          size="sm"
          variant="outline"
          :disabled="!canPublish"
          @click="emit('publish')"
        >
          {{ $t("marketplace.connectionPage.products.setupChoose") }}
          <ChevronRight class="ml-1 size-4" />
        </Button>
      </template>
    </InsetGroupItem>
  </InsetGroup>
</template>
