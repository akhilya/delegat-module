<script setup>
import { computed, onMounted, ref } from "vue";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
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
  applyQuotaSnapshot,
  notifyQuotaAttentionRefresh,
} from "@/lib/quotaAttention";
import {
  Boxes,
  ChevronDown,
  Files,
  HardDrive,
  Plug,
  RefreshCw,
  Trash2,
} from "lucide-vue-next";

const METRIC_ICONS = {
  products: Boxes,
  files: Files,
  storage: HardDrive,
  connections: Plug,
};

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

const overageHint = computed(() => {
  if (overageActive.value && purgeAtLabel.value) {
    return t("pages.usagePage.overageWithDeadline", {
      date: purgeAtLabel.value,
    });
  }
  if (purgeAtLabel.value) {
    return t("pages.usagePage.overageDeadlineOnly", {
      date: purgeAtLabel.value,
    });
  }
  return t("pages.usagePage.overageNoDeadline");
});

const instanceRows = computed(() => metricRows(snapshot.value));

const previewGroups = computed(() => {
  const p = preview.value;
  if (!p?.isOverQuota) return [];
  return [
    { key: "products", items: p.products || [] },
    { key: "connections", items: p.connections || [] },
    { key: "files", items: p.files || [] },
  ].filter((group) => group.items.length);
});

function metricRows(source) {
  if (!source) return [];
  return [
    { key: "products", metric: source.products, bytes: false },
    { key: "files", metric: source.files, bytes: false },
    { key: "storage", metric: source.storageBytes, bytes: true },
    { key: "connections", metric: source.connections, bytes: false },
  ];
}

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

function formatCompact(metric, { bytes = false } = {}) {
  if (!metric) return "—";
  const used = bytes ? formatBytes(metric.used) : String(metric.used);
  if (metric.limit == null)
    return `${used} · ${t("pages.usagePage.unlimited")}`;
  const limit = bytes ? formatBytes(metric.limit) : String(metric.limit);
  return t("pages.usagePage.usedOfLimitCompact", { used, limit });
}

function formatDetail(metric, { bytes = false } = {}) {
  if (!metric) return "—";
  const used = bytes ? formatBytes(metric.used) : String(metric.used);
  if (metric.limit == null)
    return `${used} · ${t("pages.usagePage.unlimited")}`;
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
    applyQuotaSnapshot(snapshot.value);
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
    notifyQuotaAttentionRefresh();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isTrimming.value = false;
  }
}

onMounted(load);
</script>

<template>
  <div class="flex flex-col gap-8">
    <EmptyState v-if="isLoading" loading :framed="false" class="min-h-40" />

    <template v-else-if="snapshot">
      <section>
        <InsetGroupLabel>{{ $t("pages.usagePage.planLabel") }}</InsetGroupLabel>
        <InsetGroup>
          <InsetGroupItem as="div">
            <p>{{ snapshot.planName || "—" }}</p>
            <template v-if="snapshot.platformLinked" #trailing>
              <span class="text-muted-foreground text-sm font-normal">
                {{ $t("pages.usagePage.platformLinked") }}
              </span>
            </template>
          </InsetGroupItem>
          <InsetGroupItem :disabled="isTrimming" @click="load">
            <template #leading>
              <RefreshCw class="size-4" />
            </template>
            <p>{{ $t("common.refresh") }}</p>
          </InsetGroupItem>
          <InsetGroupItem
            v-if="isOver"
            :disabled="isTrimming"
            class="text-destructive hover:bg-destructive/10 hover:text-destructive"
            @click="trimNow"
          >
            <template #leading>
              <Spinner v-if="isTrimming" size="sm" />
              <Trash2 v-else class="size-4" />
            </template>
            <p>{{ $t("pages.usagePage.trimNow") }}</p>
          </InsetGroupItem>
        </InsetGroup>
        <p
          v-if="isOver"
          class="mt-2 px-1 text-xs leading-relaxed text-destructive"
        >
          {{ overageHint }}
        </p>
      </section>

      <section>
        <InsetGroupLabel>{{
          $t("pages.usagePage.instanceTotals")
        }}</InsetGroupLabel>
        <InsetGroup>
          <InsetGroupItem
            v-for="row in instanceRows"
            :key="row.key"
            as="div"
            :title="formatDetail(row.metric, { bytes: row.bytes })"
          >
            <template #leading>
              <component :is="METRIC_ICONS[row.key]" class="size-4" />
            </template>
            <p>{{ $t(`pages.usagePage.metrics.${row.key}`) }}</p>
            <div
              class="mt-1.5 h-1 overflow-hidden rounded-md bg-muted"
              aria-hidden="true"
            >
              <div
                class="h-full rounded-md transition-[width] duration-300 ease-out"
                :class="row.metric?.isOver ? 'bg-destructive' : 'bg-primary'"
                :style="{ width: `${metricPercent(row.metric)}%` }"
              />
            </div>
            <template #trailing>
              <span
                class="text-xs font-normal tabular-nums"
                :class="
                  row.metric?.isOver
                    ? 'text-destructive'
                    : 'text-muted-foreground'
                "
              >
                {{ formatCompact(row.metric, { bytes: row.bytes }) }}
              </span>
            </template>
          </InsetGroupItem>
          <p
            v-if="snapshot.stores?.length"
            class="px-3 pb-1 pt-3 text-xs font-medium text-muted-foreground"
          >
            {{ $t("pages.usagePage.perStore") }}
          </p>
          <Collapsible
            v-for="store in snapshot.stores"
            :key="store.storeId"
            class="group/store"
          >
            <CollapsibleTrigger as-child>
              <InsetGroupItem>
                <p class="truncate">{{ store.storeName }}</p>
                <template #trailing>
                  <span
                    v-if="store.isOverQuota"
                    class="text-xs font-normal text-destructive"
                  >
                    {{ $t("pages.usagePage.over") }}
                  </span>
                  <ChevronDown
                    class="size-4 text-muted-foreground opacity-40 transition-transform duration-200 group-data-[state=open]/store:rotate-180"
                  />
                </template>
              </InsetGroupItem>
            </CollapsibleTrigger>
            <CollapsibleContent>
              <div class="space-y-2 px-3 pb-2.5 pt-0.5">
                <div
                  v-for="row in metricRows(store)"
                  :key="row.key"
                  class="flex items-center justify-between gap-3 text-xs"
                >
                  <span class="text-muted-foreground">
                    {{ $t(`pages.usagePage.metrics.${row.key}`) }}
                  </span>
                  <span
                    class="tabular-nums"
                    :class="
                      row.metric?.isOver
                        ? 'text-destructive'
                        : 'text-foreground'
                    "
                  >
                    {{ formatCompact(row.metric, { bytes: row.bytes }) }}
                  </span>
                </div>
              </div>
            </CollapsibleContent>
          </Collapsible>
          <div v-if="previewGroups.length" class="px-3 pb-1 pt-3">
            <p class="text-xs font-medium text-muted-foreground">
              {{ $t("pages.usagePage.previewTitle") }}
            </p>
            <p class="mt-1 text-xs leading-relaxed text-muted-foreground">
              {{ $t("pages.usagePage.previewDescription") }}
            </p>
          </div>
          <Collapsible
            v-for="group in previewGroups"
            :key="group.key"
            class="group/preview"
          >
            <CollapsibleTrigger as-child>
              <InsetGroupItem>
                <template #leading>
                  <component
                    :is="METRIC_ICONS[group.key] || Files"
                    class="size-4"
                  />
                </template>
                <p>
                  {{
                    $t(`pages.usagePage.previewGroups.${group.key}`, {
                      count: group.items.length,
                    })
                  }}
                </p>
                <template #trailing>
                  <ChevronDown
                    class="size-4 text-muted-foreground opacity-40 transition-transform duration-200 group-data-[state=open]/preview:rotate-180"
                  />
                </template>
              </InsetGroupItem>
            </CollapsibleTrigger>
            <CollapsibleContent>
              <ul class="max-h-48 space-y-2 overflow-y-auto px-3 pb-2.5 pt-0.5">
                <li
                  v-for="item in group.items"
                  :key="`${item.kind}-${item.id}`"
                  class="flex items-start justify-between gap-2 text-xs"
                >
                  <div class="min-w-0">
                    <p class="truncate font-medium">{{ item.label }}</p>
                    <p class="text-muted-foreground">
                      {{ item.storeName }}
                      <span v-if="item.isTrashed">
                        · {{ $t("pages.usagePage.trashed") }}
                      </span>
                    </p>
                  </div>
                  <span
                    v-if="item.bytes != null"
                    class="shrink-0 tabular-nums text-muted-foreground"
                  >
                    {{ formatBytes(item.bytes) }}
                  </span>
                </li>
              </ul>
            </CollapsibleContent>
          </Collapsible>
        </InsetGroup>
      </section>
    </template>
  </div>
</template>
