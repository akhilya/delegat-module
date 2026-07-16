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

const { formatDateTime, loadMemberships } = useStoreDateTime();
loadMemberships();

const breadcrumbs = [
  { label: "Настройки", to: "/settings" },
  { label: "Очереди и процессы" },
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
  if (value === "running") return "выполняется";
  if (value === "paused") return "пауза";
  if (value === "completed") return "завершено";
  if (value === "failed") return "ошибка";
  if (value === "cancelled") return "отменено";
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
    "Очистить очередь",
    destructiveAll
      ? "Очистить все записи выбранной очереди, включая ожидающие и выполняющиеся?"
      : "Очистить завершённые, ошибочные и отменённые записи?"
  );
  if (!confirmed) return;

  clearingKey.value = `${target}:${status}`;
  try {
    const response = await http.post("/settings/operations/clear", { target, status }, { cache: false });
    displaySonnerSuccess(`Удалено записей: ${response.data?.deleted ?? 0}`);
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
      title="Очереди и процессы"
      description="Управление фоновыми процессами, экспортом, импортом, изображениями и переносами"
    >
      <Button variant="outline" :disabled="isLoading" @click="fetchData">
        <Loader2 v-if="isLoading" class="mr-2 size-4 animate-spin" />
        <RefreshCw v-else class="mr-2 size-4" />
        Обновить
      </Button>
    </PageHeader>

    <PageSection :loading="isLoading" class="space-y-4">
      <div class="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle class="flex items-center gap-2">
              <Activity class="size-4 text-muted-foreground" />
              История фоновых процессов
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
              <Badge v-if="!summary?.operations?.total" variant="secondary">нет записей</Badge>
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
                Очистить завершённые
              </Button>
              <Button
                variant="destructive"
                size="sm"
                :disabled="clearingKey === 'operations:all'"
                @click="clearTarget('operations', 'all')"
              >
                <Loader2 v-if="clearingKey === 'operations:all'" class="mr-2 size-4 animate-spin" />
                <Trash2 v-else class="mr-2 size-4" />
                Очистить всё
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
              <Badge v-if="queue.total === 0" variant="secondary">нет записей</Badge>
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
                Очистить неактивные
              </Button>
              <Button
                variant="destructive"
                size="sm"
                :disabled="queue.total === 0 || clearingKey === `${queue.key}:all`"
                @click="clearTarget(queue.key, 'all')"
              >
                <Loader2 v-if="clearingKey === `${queue.key}:all`" class="mr-2 size-4 animate-spin" />
                <Trash2 v-else class="mr-2 size-4" />
                Очистить всё
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>

      <div class="overflow-hidden rounded-lg border bg-card">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Процесс</TableHead>
              <TableHead class="w-32">Статус</TableHead>
              <TableHead class="w-40">Тип</TableHead>
              <TableHead class="w-40">Прогресс</TableHead>
              <TableHead class="w-40">Создан</TableHead>
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
                  ({{ operation.failedItems }} ошибок)
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
                  title="История пуста"
                  description="Здесь появятся завершённые операции: экспорт, импорт и AI-задачи."
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
          <AlertDialogCancel @click="confirmDialog.resolve(false); confirmDialog.open = false">Отмена</AlertDialogCancel>
          <AlertDialogAction @click="confirmDialog.resolve(true); confirmDialog.open = false">Очистить</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </Page>
</template>
