<script setup>
import { computed, onMounted, ref } from "vue";
import { Button } from "@/components/ui/button";
import { Progress } from "@/components/ui/progress";
import { Badge } from "@/components/ui/badge";
import { Spinner } from "@/components/ui/spinner";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { t } from "@/i18n";
import { confirm } from "@/composables/useConfirm";
import {
  AlertTriangle,
  ChevronDown,
  RefreshCw,
  Trash2,
} from "lucide-vue-next";

const isLoading = ref(true);
const isTrimming = ref(false);
const snapshot = ref(null);
const preview = ref(null);

const isOver = computed(() => snapshot.value?.isOverQuota === true);
const overageActive = computed(() => snapshot.value?.overageActive === true);
const purgeAtLabel = computed(() => {
  const at = snapshot.value?.overagePurgeAt || preview.value?.overagePurgeAt;
  if (!at) return null;
  try {
    return new Date(at).toLocaleString();
  } catch {
    return String(at);
  }
});

const instanceRows = computed(() => {
  const s = snapshot.value;
  if (!s) return [];
  return [
    { key: "products", metric: s.products, bytes: false },
    { key: "files", metric: s.files, bytes: false },
    { key: "storage", metric: s.storageBytes, bytes: true },
    { key: "connections", metric: s.connections, bytes: false },
  ];
});

function metricPercent(metric) {
  if (!metric?.limit || metric.limit <= 0) return 0;
  return Math.min(100, Math.round((metric.used / metric.limit) * 100));
}

function formatBytes(bytes) {
  const n = Number(bytes) || 0;
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / (1024 * 1024)).toFixed(1)} MB`;
  return `${(n / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

function formatMetric(metric, { bytes = false } = {}) {
  if (!metric) return "—";
  const used = bytes ? formatBytes(metric.used) : String(metric.used);
  if (metric.limit == null) return `${used} · ${t("pages.usagePage.unlimited")}`;
  const limit = bytes ? formatBytes(metric.limit) : String(metric.limit);
  const remaining = bytes
    ? formatBytes(metric.remaining ?? 0)
    : String(metric.remaining ?? 0);
  return t("pages.usagePage.usedOfLimit", { used, limit, remaining });
}

async function load() {
  isLoading.value = true;
  try {
    const [usageRes, previewRes] = await Promise.all([
      http.get("/settings/quotas", { cache: false }),
      http.get("/settings/quotas/overage-preview", { cache: false }),
    ]);
    snapshot.value = usageRes.data?.value ?? usageRes.data;
    preview.value = previewRes.data?.value ?? previewRes.data;
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isLoading.value = false;
  }
}

async function trimNow() {
  const ok = await confirm({
    title: t("pages.usagePage.trimConfirmTitle"),
    description: t("pages.usagePage.trimConfirmDescription"),
    confirmLabel: t("pages.usagePage.trimNow"),
    destructive: true,
  });
  if (!ok) return;

  isTrimming.value = true;
  try {
    const res = await http.post("/settings/quotas/trim", {}, { cache: false });
    const result = res.data?.value ?? res.data;
    displaySonnerSuccess(
      t("pages.usagePage.trimDone", {
        products: result.productsRemoved ?? 0,
        connections: result.connectionsRemoved ?? 0,
        files: result.filesRemoved ?? 0,
      }),
    );
    await load();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isTrimming.value = false;
  }
}

onMounted(load);
</script>

<template>
  <div class="space-y-4">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div v-if="snapshot?.planName" class="flex flex-wrap items-center gap-2 text-sm">
        <span class="text-muted-foreground">{{ $t("pages.usagePage.planLabel") }}</span>
        <Badge variant="secondary">{{ snapshot.planName }}</Badge>
        <Badge v-if="snapshot.platformLinked" variant="outline">
          {{ $t("pages.usagePage.platformLinked") }}
        </Badge>
      </div>
      <div v-else class="flex-1" />
      <div class="flex gap-2">
        <Button
          variant="outline"
          size="sm"
          :disabled="isLoading || isTrimming"
          @click="load"
        >
          <RefreshCw class="mr-1.5 size-3.5" />
          {{ $t("common.refresh") }}
        </Button>
        <Button
          v-if="isOver"
          variant="destructive"
          size="sm"
          :disabled="isLoading || isTrimming"
          @click="trimNow"
        >
          <Spinner v-if="isTrimming" class="mr-1.5" />
          <Trash2 v-else class="mr-1.5 size-3.5" />
          {{ $t("pages.usagePage.trimNow") }}
        </Button>
      </div>
    </div>

    <div v-if="isLoading" class="flex h-24 items-center justify-center">
      <Spinner size="xl" class="text-muted-foreground" />
    </div>

    <div v-else-if="snapshot" class="space-y-4">
      <div
        v-if="isOver"
        class="rounded-md border border-destructive/40 bg-destructive/5 px-3 py-2.5"
      >
        <div class="flex items-start gap-2">
          <AlertTriangle class="mt-0.5 size-4 shrink-0 text-destructive" />
          <div class="space-y-1">
            <p class="text-sm font-medium text-destructive">
              {{ $t("pages.usagePage.overageTitle") }}
            </p>
            <p class="text-xs text-muted-foreground">
              <template v-if="overageActive && purgeAtLabel">
                {{ $t("pages.usagePage.overageWithDeadline", { date: purgeAtLabel }) }}
              </template>
              <template v-else-if="purgeAtLabel">
                {{ $t("pages.usagePage.overageDeadlineOnly", { date: purgeAtLabel }) }}
              </template>
              <template v-else>
                {{ $t("pages.usagePage.overageNoDeadline") }}
              </template>
            </p>
          </div>
        </div>
      </div>

      <div class="space-y-3">
        <p class="text-xs font-medium text-muted-foreground">
          {{ $t("pages.usagePage.instanceTotals") }}
        </p>
        <div class="space-y-2.5">
          <div
            v-for="row in instanceRows"
            :key="row.key"
            class="space-y-1"
          >
            <div class="flex items-center justify-between gap-2 text-sm">
              <span class="font-medium">{{ $t(`pages.usagePage.metrics.${row.key}`) }}</span>
              <div class="flex items-center gap-2">
                <Badge v-if="row.metric?.isOver" variant="destructive" class="h-5 px-1.5 text-[10px]">
                  {{ $t("pages.usagePage.over") }}
                </Badge>
                <span class="tabular-nums text-xs text-muted-foreground">
                  {{ formatMetric(row.metric, { bytes: row.bytes }) }}
                </span>
              </div>
            </div>
            <Progress
              :model-value="metricPercent(row.metric)"
              class="h-1.5"
              :class="row.metric?.isOver ? '[&_[data-slot]]:bg-destructive' : ''"
            />
          </div>
        </div>
      </div>

      <div v-if="snapshot.stores?.length" class="space-y-2">
        <p class="text-xs font-medium text-muted-foreground">
          {{ $t("pages.usagePage.perStore") }}
        </p>
        <Collapsible
          v-for="store in snapshot.stores"
          :key="store.storeId"
          class="rounded-md border"
        >
          <CollapsibleTrigger class="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm hover:bg-muted/40">
            <span class="font-medium">{{ store.storeName }}</span>
            <div class="flex items-center gap-2">
              <Badge v-if="store.isOverQuota" variant="destructive" class="h-5 px-1.5 text-[10px]">
                {{ $t("pages.usagePage.over") }}
              </Badge>
              <ChevronDown class="size-3.5 text-muted-foreground" />
            </div>
          </CollapsibleTrigger>
          <CollapsibleContent>
            <div class="space-y-2 border-t px-3 py-2.5">
              <div
                v-for="row in [
                  { key: 'products', metric: store.products, bytes: false },
                  { key: 'files', metric: store.files, bytes: false },
                  { key: 'storage', metric: store.storageBytes, bytes: true },
                  { key: 'connections', metric: store.connections, bytes: false },
                ]"
                :key="row.key"
                class="space-y-1"
              >
                <div class="flex justify-between text-xs">
                  <span>{{ $t(`pages.usagePage.metrics.${row.key}`) }}</span>
                  <span class="tabular-nums text-muted-foreground">
                    {{ formatMetric(row.metric, { bytes: row.bytes }) }}
                  </span>
                </div>
                <Progress :model-value="metricPercent(row.metric)" class="h-1" />
              </div>
            </div>
          </CollapsibleContent>
        </Collapsible>
      </div>

      <div v-if="preview?.isOverQuota" class="space-y-2">
        <p class="text-xs font-medium text-muted-foreground">
          {{ $t("pages.usagePage.previewTitle") }}
        </p>
        <p class="text-xs text-muted-foreground">
          {{ $t("pages.usagePage.previewDescription") }}
        </p>
        <Collapsible
          v-for="group in [
            { key: 'products', items: preview.products || [] },
            { key: 'connections', items: preview.connections || [] },
            { key: 'files', items: preview.files || [] },
          ].filter((g) => g.items.length)"
          :key="group.key"
          class="rounded-md border"
        >
          <CollapsibleTrigger class="flex w-full items-center justify-between px-3 py-2 text-left text-sm font-medium hover:bg-muted/40">
            <span>
              {{ $t(`pages.usagePage.previewGroups.${group.key}`, { count: group.items.length }) }}
            </span>
            <ChevronDown class="size-3.5 text-muted-foreground" />
          </CollapsibleTrigger>
          <CollapsibleContent>
            <ul class="max-h-48 divide-y overflow-y-auto border-t">
              <li
                v-for="item in group.items"
                :key="`${item.kind}-${item.id}`"
                class="flex items-start justify-between gap-2 px-3 py-1.5 text-xs"
              >
                <div class="min-w-0">
                  <div class="truncate font-medium">{{ item.label }}</div>
                  <div class="text-muted-foreground">
                    {{ item.storeName }}
                    <span v-if="item.isTrashed"> · {{ $t("pages.usagePage.trashed") }}</span>
                  </div>
                </div>
                <span v-if="item.bytes != null" class="shrink-0 tabular-nums text-muted-foreground">
                  {{ formatBytes(item.bytes) }}
                </span>
              </li>
            </ul>
          </CollapsibleContent>
        </Collapsible>
      </div>
    </div>
  </div>
</template>
