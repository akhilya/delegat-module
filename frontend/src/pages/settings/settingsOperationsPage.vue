<script setup>
import { computed, onMounted, ref } from "vue";
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import { Button } from "@/components/ui/button";
import Badge from "@/components/ui/badge/Badge.vue";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { PageHeader } from "@/components/ui/page-header";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { http } from "@/main";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Activity, Loader2, RefreshCw, Trash2 } from "lucide-vue-next";
import { EmptyState } from "@/components/ui/empty-state";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { useStoreDateTime } from "@/composables/useStoreDateTime";
import { t } from "@/i18n";

const { formatDateTime, loadMemberships } = useStoreDateTime();
loadMemberships();

const breadcrumbs = [
  { label: t("routes.settings"), to: "/settings" },
  { label: t("pages.settingsOperationsPage.title") },
];

const summary = ref(null);
const operations = ref([]);
const isLoading = ref(false);
const clearingKey = ref(null);
const confirmDialog = ref({ open: false, title: "", message: "", resolve: null });

function showConfirm(title, message) {
  return new Promise((r) => { confirmDialog.value = { open: true, title, message, resolve: r }; });
}

const queues = computed(() => summary.value?.queues || []);

function statusCount(item, status) {
  return item.statuses?.find((entry) => entry.status?.toLowerCase() === status)?.count || 0;
}

function inactiveCount(item) {
  return ["completed", "failed", "cancelled", "partial"]
    .reduce((sum, status) => sum + statusCount(item, status), 0);
}

function formatDate(value) {
  if (!value) return "-";
  const formatted = formatDateTime(value);
  return formatted === "—" ? "-" : formatted;
}

function statusLabel(status) {
  const value = String(status || "").toLowerCase();
  if (value === "running") return t("operations.status.running");
  if (value === "paused") return t("operations.status.paused");
  if (value === "completed") return t("operations.status.completed");
  if (value === "failed") return t("operations.status.failed");
  if (value === "cancelled") return t("operations.status.cancelled");
  return status || "-";
}

async function fetchData() {
  isLoading.value = true;
  try {
    const [summaryResponse, operationsResponse] = await Promise.all([
      http.get("/settings/operations/summary", { cache: false }),
      http.get("/operations", { params: { page: 1, pageSize: 50 }, cache: false }),
    ]);
    summary.value = summaryResponse.data;
    operations.value = operationsResponse.data?.items || [];
  } catch (error) {
    displaySonnerError(error);
  } finally {
    isLoading.value = false;
  }
}

async function clearTarget(target, status = "inactive") {
  const destructiveAll = status === "all";
  const confirmed = await showConfirm(
    t("pages.settingsOperationsPage.clearQueueTitle"),
    destructiveAll
      ? t("pages.settingsOperationsPage.clearAllDescription")
      : t("pages.settingsOperationsPage.clearInactiveDescription")
  );
  if (!confirmed) return;

  clearingKey.value = `${target}:${status}`;
  try {
    const response = await http.post("/settings/operations/clear", { target, status }, { cache: false });
    displaySonnerSuccess(t("pages.settingsOperationsPage.deletedRecords", { count: response.data?.deleted ?? 0 }));
    await fetchData();
  } catch (error) {
    displaySonnerError(error);
  } finally {
    clearingKey.value = null;
  }
}

onMounted(fetchData);
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader
      :title="$t('pages.settingsOperationsPage.title')"
      :description="$t('pages.settingsOperationsPage.description')"
    >
      <Button variant="outline" :disabled="isLoading" @click="fetchData">
        <Loader2 v-if="isLoading" class="mr-2 size-4 animate-spin" />
        <RefreshCw v-else class="mr-2 size-4" />
        {{ $t("common.refresh") }}
      </Button>
    </PageHeader>

    <PageSection :loading="isLoading" class="space-y-4">
      <div class="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle class="flex items-center gap-2">
              <Activity class="size-4 text-muted-foreground" />
              {{ $t("pages.settingsOperationsPage.historyTitle") }}
            </CardTitle>
          </CardHeader>
          <CardContent class="space-y-3">
            <div class="flex flex-wrap gap-2">
              <Badge
                v-for="entry in summary?.operations?.statuses || []"
                :key="entry.status"
                variant="secondary"
              >
                {{ statusLabel(entry.status) }}: {{ entry.count }}
              </Badge>
              <Badge v-if="!summary?.operations?.total" variant="secondary">{{ $t("pages.settingsOperationsPage.noRecords") }}</Badge>
            </div>
            <div class="flex flex-wrap gap-2">
              <Button
                variant="outline"
                size="sm"
                :disabled="clearingKey === 'operations:inactive'"
                @click="clearTarget('operations', 'inactive')"
              >
                <Loader2 v-if="clearingKey === 'operations:inactive'" class="mr-2 size-4 animate-spin" />
                <Trash2 v-else class="mr-2 size-4" />
                {{ $t("pages.settingsOperationsPage.clearCompletedButton") }}
              </Button>
              <Button
                variant="destructive"
                size="sm"
                :disabled="clearingKey === 'operations:all'"
                @click="clearTarget('operations', 'all')"
              >
                <Loader2 v-if="clearingKey === 'operations:all'" class="mr-2 size-4 animate-spin" />
                <Trash2 v-else class="mr-2 size-4" />
                {{ $t("pages.settingsOperationsPage.clearAllButton") }}
              </Button>
            </div>
          </CardContent>
        </Card>

        <Card v-for="queue in queues" :key="queue.key">
          <CardHeader>
            <CardTitle class="flex items-center justify-between gap-3">
              <span>{{ queue.label }}</span>
              <Badge variant="secondary">{{ queue.total }}</Badge>
            </CardTitle>
          </CardHeader>
          <CardContent class="space-y-3">
            <div class="flex flex-wrap gap-2">
              <Badge
                v-for="entry in queue.statuses"
                :key="entry.status"
                variant="secondary"
              >
                {{ statusLabel(entry.status) }}: {{ entry.count }}
              </Badge>
              <Badge v-if="queue.total === 0" variant="secondary">{{ $t("pages.settingsOperationsPage.noRecords") }}</Badge>
            </div>
            <div class="flex flex-wrap gap-2">
              <Button
                variant="outline"
                size="sm"
                :disabled="inactiveCount(queue) === 0 || clearingKey === `${queue.key}:inactive`"
                @click="clearTarget(queue.key, 'inactive')"
              >
                <Loader2 v-if="clearingKey === `${queue.key}:inactive`" class="mr-2 size-4 animate-spin" />
                <Trash2 v-else class="mr-2 size-4" />
                {{ $t("pages.settingsOperationsPage.clearInactiveButton") }}
              </Button>
              <Button
                variant="destructive"
                size="sm"
                :disabled="queue.total === 0 || clearingKey === `${queue.key}:all`"
                @click="clearTarget(queue.key, 'all')"
              >
                <Loader2 v-if="clearingKey === `${queue.key}:all`" class="mr-2 size-4 animate-spin" />
                <Trash2 v-else class="mr-2 size-4" />
                {{ $t("pages.settingsOperationsPage.clearAllButton") }}
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>

      <div class="overflow-hidden rounded-lg border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{{ $t("pages.settingsOperationsPage.columnProcess") }}</TableHead>
              <TableHead class="w-32">{{ $t("common.status") }}</TableHead>
              <TableHead class="w-40">{{ $t("pages.settingsOperationsPage.columnType") }}</TableHead>
              <TableHead class="w-40">{{ $t("pages.settingsOperationsPage.columnProgress") }}</TableHead>
              <TableHead class="w-40">{{ $t("pages.settingsOperationsPage.columnCreated") }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="operation in operations" :key="operation.operationId">
              <TableCell>
                <div class="font-medium">{{ operation.title }}</div>
                <div class="text-xs text-muted-foreground">{{ operation.connectionName || operation.operationId }}</div>
              </TableCell>
              <TableCell>
                <Badge variant="secondary">{{ statusLabel(operation.status) }}</Badge>
              </TableCell>
              <TableCell class="text-sm text-muted-foreground">
                {{ operation.batchType || operation.type }}
              </TableCell>
              <TableCell class="text-sm tabular-nums">
                {{ operation.completedItems || 0 }} / {{ operation.totalItems || 0 }}
                <span v-if="operation.failedItems" class="text-destructive">
                  {{ $t("pages.settingsOperationsPage.failedItemsSuffix", { count: operation.failedItems }) }}
                </span>
              </TableCell>
              <TableCell class="text-sm text-muted-foreground">
                {{ formatDate(operation.createdAt) }}
              </TableCell>
            </TableRow>
            <TableRow v-if="operations.length === 0">
              <TableCell colspan="5">
                <EmptyState
                  size="sm"
                  :title="$t('pages.settingsOperationsPage.emptyHistoryTitle')"
                  :description="$t('pages.settingsOperationsPage.emptyHistoryDescription')"
                >
                  <template #icon>
                    <Activity />
                  </template>
                </EmptyState>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </PageSection>

    <AlertDialog
      :open="confirmDialog.open"
      @update:open="(v) => { if (!v) confirmDialog.resolve?.(false); confirmDialog.open = v; }"
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmDialog.title }}</AlertDialogTitle>
          <AlertDialogDescription>{{ confirmDialog.message }}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel @click="confirmDialog.resolve(false); confirmDialog.open = false">{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction @click="confirmDialog.resolve(true); confirmDialog.open = false">{{ $t("operations.clearCompletedConfirm") }}</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </Page>
</template>
