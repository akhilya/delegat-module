<script setup>
import { computed } from "vue";
import {
  AlertCircle,
  Check,
  ChevronLeft,
  ChevronRight,
  Search,
} from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import Checkbox from "@/components/ui/checkbox/Checkbox.vue";
import Input from "@/components/ui/input/Input.vue";
import { Spinner } from "@/components/ui/spinner";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";

const props = defineProps({
  open: { type: Boolean, default: false },
  adapterName: { type: String, default: "" },
  step: { type: String, default: "categories" },
  unmappedCategories: { type: Array, default: () => [] },
  selectedCount: { type: Number, default: 0 },
  search: { type: String, default: "" },
  products: { type: Array, default: () => [] },
  isLoadingProducts: { type: Boolean, default: false },
  selectedIds: { type: Array, default: () => [] },
  page: { type: Number, default: 1 },
  lastPage: { type: Number, default: 1 },
  readiness: { type: Object, default: null },
  isChecking: { type: Boolean, default: false },
  isPublishing: { type: Boolean, default: false },
  error: { type: String, default: null },
});

const emit = defineEmits([
  "update:open",
  "update:search",
  "create-mapping",
  "toggle-product",
  "change-page",
  "change-step",
  "continue",
  "check",
  "publish",
  "cancel",
]);

const steps = computed(() => [
  {
    id: "categories",
    label: "marketplace.connectionPage.products.publishWizardCategories",
  },
  {
    id: "products",
    label: "marketplace.connectionPage.products.publishWizardProducts",
  },
  {
    id: "review",
    label: "marketplace.connectionPage.products.publishWizardReview",
  },
]);

const activeStepIndex = computed(() =>
  steps.value.findIndex((item) => item.id === props.step),
);

const blockedProducts = computed(
  () => props.readiness?.products?.filter((product) => !product.isReady) ?? [],
);

function isSelected(productId) {
  return (
    props.selectedIds.includes(productId) ||
    props.selectedIds.includes(String(productId))
  );
}
</script>

<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent
      size="2xl"
      class="max-h-[min(90dvh,820px)] overflow-hidden p-0 sm:rounded-xl"
    >
      <div class="flex max-h-[min(90dvh,820px)] flex-col">
        <DialogHeader class="border-b px-6 py-5 pr-12">
          <p
            class="mb-1 text-xs font-medium uppercase tracking-wide text-muted-foreground"
          >
            {{ adapterName }}
          </p>
          <DialogTitle class="text-base font-semibold tracking-tight">
            {{
              $t("marketplace.connectionPage.products.publishWizardTitle", {
                adapter: adapterName,
              })
            }}
          </DialogTitle>
          <DialogDescription class="max-w-prose text-sm leading-relaxed">
            {{
              $t("marketplace.connectionPage.products.publishWizardDescription")
            }}
          </DialogDescription>
        </DialogHeader>

        <nav aria-label="Steps" class="border-b bg-muted/30 px-6 py-3">
          <ol class="grid grid-cols-3 gap-2">
            <li v-for="(item, index) in steps" :key="item.id" class="min-w-0">
              <Button
                variant="ghost"
                class="h-auto w-full justify-start gap-2 px-2 py-1.5 text-left text-xs font-normal"
                :class="
                  index === activeStepIndex
                    ? 'bg-background font-semibold text-foreground ring-1 ring-border'
                    : index < activeStepIndex
                      ? 'text-foreground'
                      : 'text-muted-foreground'
                "
                :aria-current="index === activeStepIndex ? 'step' : undefined"
                :disabled="index >= activeStepIndex"
                @click="emit('change-step', item.id)"
              >
                <span
                  class="flex size-6 shrink-0 items-center justify-center rounded-full border text-[11px] tabular-nums"
                  :class="
                    index < activeStepIndex
                      ? 'border-foreground bg-foreground text-background'
                      : index === activeStepIndex
                        ? 'border-foreground text-foreground'
                        : 'border-border text-muted-foreground'
                  "
                >
                  <Check v-if="index < activeStepIndex" class="size-3.5" />
                  <template v-else>{{ index + 1 }}</template>
                </span>
                <span class="truncate">{{ $t(item.label) }}</span>
              </Button>
            </li>
          </ol>
        </nav>

        <main class="min-h-0 flex-1 overflow-y-auto px-6 py-5">
          <section v-if="step === 'categories'" class="space-y-4">
            <div class="space-y-1">
              <h3 class="text-sm font-medium">
                {{
                  $t(
                    "marketplace.connectionPage.products.publishWizardCategories",
                  )
                }}
              </h3>
              <p class="text-sm leading-relaxed text-muted-foreground">
                {{
                  $t(
                    "marketplace.connectionPage.products.publishWizardCategoryHint",
                    { adapter: adapterName },
                  )
                }}
              </p>
            </div>

            <InsetGroup v-if="unmappedCategories.length">
              <InsetGroupItem
                v-for="category in unmappedCategories"
                :key="category.id"
                as="div"
                class="min-h-14"
              >
                <div class="min-w-0">
                  <p class="truncate font-medium">{{ category.name }}</p>
                </div>
                <template #trailing>
                  <Button
                    size="sm"
                    variant="outline"
                    @click="emit('create-mapping', category)"
                  >
                    {{
                      $t(
                        "marketplace.connectionPage.products.publishWizardChooseCategory",
                      )
                    }}
                  </Button>
                </template>
              </InsetGroupItem>
            </InsetGroup>

            <InsetGroup v-else>
              <InsetGroupItem as="div" class="min-h-14">
                <template #leading>
                  <span
                    class="flex size-8 items-center justify-center rounded-full bg-success-muted text-success-foreground"
                  >
                    <Check class="size-4" />
                  </span>
                </template>
                <p class="font-medium">
                  {{
                    $t(
                      "marketplace.connectionPage.products.publishWizardCategoriesDone",
                    )
                  }}
                </p>
              </InsetGroupItem>
            </InsetGroup>

            <p
              v-if="unmappedCategories.length"
              class="text-xs leading-relaxed text-muted-foreground"
            >
              {{
                $t(
                  "marketplace.connectionPage.products.publishWizardCategoryOptional",
                )
              }}
            </p>
          </section>

          <section v-else-if="step === 'products'" class="space-y-4">
            <div class="flex flex-wrap items-end justify-between gap-3">
              <div class="space-y-1">
                <h3 class="text-sm font-medium">
                  {{
                    $t(
                      "marketplace.connectionPage.products.publishWizardProducts",
                    )
                  }}
                </h3>
                <p class="text-sm text-muted-foreground">
                  {{ $t("marketplace.connectionPage.products.selectedLabel") }}
                  <span class="font-medium tabular-nums text-foreground">{{
                    selectedCount
                  }}</span>
                </p>
              </div>
              <div class="relative w-full sm:w-64">
                <Search
                  class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
                />
                <Input
                  :model-value="search"
                  class="pl-8"
                  :placeholder="
                    $t(
                      'marketplace.connectionPage.products.publishWizardSearch',
                    )
                  "
                  @update:model-value="emit('update:search', $event)"
                />
              </div>
            </div>

            <div
              v-if="isLoadingProducts"
              class="flex min-h-56 items-center justify-center gap-2 text-sm text-muted-foreground"
            >
              <Spinner />
              {{ $t("common.loading") }}
            </div>
            <InsetGroup
              v-else-if="products.length"
              class="max-h-[min(48vh,460px)] overflow-y-auto"
            >
              <InsetGroupItem
                v-for="product in products"
                :key="product.id"
                as="div"
                class="min-h-12"
                :class="isSelected(product.id) ? 'bg-muted/60' : ''"
              >
                <template #leading>
                  <Checkbox
                    :model-value="isSelected(product.id)"
                    :aria-label="product.name || `#${product.id}`"
                    @update:model-value="
                      emit('toggle-product', product.id, $event)
                    "
                  />
                </template>
                <div class="min-w-0">
                  <p class="truncate font-medium">
                    {{ product.name || `#${product.id}` }}
                  </p>
                  <p
                    v-if="product.sku"
                    class="mt-0.5 truncate text-xs text-muted-foreground"
                  >
                    {{ product.sku }}
                  </p>
                </div>
              </InsetGroupItem>
            </InsetGroup>
            <div
              v-else
              class="flex min-h-48 flex-col items-center justify-center gap-2 rounded-lg border border-dashed px-6 text-center"
            >
              <p class="text-sm font-medium">
                {{ $t("marketplace.connectionPage.products.noLocalProducts") }}
              </p>
            </div>

            <div class="flex items-center justify-between border-t pt-3">
              <Button
                variant="ghost"
                size="sm"
                :disabled="page <= 1 || isLoadingProducts"
                @click="emit('change-page', page - 1)"
              >
                <ChevronLeft class="mr-1 size-4" />
                {{ $t("common.back") }}
              </Button>
              <span class="text-xs tabular-nums text-muted-foreground"
                >{{ page }} / {{ lastPage }}</span
              >
              <Button
                variant="ghost"
                size="sm"
                :disabled="page >= lastPage || isLoadingProducts"
                @click="emit('change-page', page + 1)"
              >
                {{ $t("common.next") }}
                <ChevronRight class="ml-1 size-4" />
              </Button>
            </div>
          </section>

          <section v-else-if="step === 'review' && readiness" class="space-y-5">
            <div class="space-y-1">
              <h3 class="text-sm font-medium">
                {{
                  $t("marketplace.connectionPage.products.publishWizardReview")
                }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{
                  $t(
                    "marketplace.connectionPage.products.publishReadyDescription",
                  )
                }}
              </p>
            </div>

            <InsetGroup>
              <InsetGroupItem as="div" class="min-h-12">
                <template #leading>
                  <span
                    class="flex size-8 items-center justify-center rounded-full bg-success-muted text-success-foreground"
                  >
                    <Check class="size-4" />
                  </span>
                </template>
                <p class="font-medium">
                  {{
                    $t(
                      "marketplace.connectionPage.products.publishReadyCount",
                      { count: readiness.readyCount },
                    )
                  }}
                </p>
              </InsetGroupItem>
              <InsetGroupItem
                v-if="readiness.blockedCount"
                as="div"
                class="min-h-12"
              >
                <template #leading>
                  <span
                    class="flex size-8 items-center justify-center rounded-full bg-warning-muted text-warning-foreground"
                  >
                    <AlertCircle class="size-4" />
                  </span>
                </template>
                <p class="font-medium">
                  {{
                    $t(
                      "marketplace.connectionPage.products.publishBlockedCount",
                      { count: readiness.blockedCount },
                    )
                  }}
                </p>
              </InsetGroupItem>
            </InsetGroup>

            <p
              v-if="!readiness.readyCount"
              class="rounded-lg border border-warning/50 bg-warning-muted/40 px-3 py-2.5 text-sm text-warning-foreground"
            >
              {{ $t("marketplace.connectionPage.products.publishNoReady") }}
            </p>

            <div v-if="blockedProducts.length" class="space-y-2">
              <p
                class="text-xs font-medium uppercase tracking-wide text-muted-foreground"
              >
                {{
                  $t("marketplace.connectionPage.products.publishBlockedTitle")
                }}
              </p>
              <InsetGroup class="max-h-60 overflow-y-auto">
                <InsetGroupItem
                  v-for="product in blockedProducts"
                  :key="product.productId"
                  as="div"
                  class="items-start"
                >
                  <div class="min-w-0 space-y-1">
                    <p class="truncate font-medium">
                      {{ product.productName || `#${product.productId}` }}
                    </p>
                    <ul
                      class="space-y-0.5 text-xs leading-relaxed text-muted-foreground"
                    >
                      <li v-for="(issue, index) in product.issues" :key="index">
                        {{ issue }}
                      </li>
                    </ul>
                  </div>
                </InsetGroupItem>
              </InsetGroup>
            </div>
          </section>

          <p
            v-if="error"
            role="alert"
            class="mt-4 rounded-lg border border-destructive/40 px-3 py-2.5 text-sm text-destructive"
          >
            {{ error }}
          </p>
        </main>

        <DialogFooter class="border-t bg-muted/20 px-6 py-4 sm:justify-between">
          <Button variant="ghost" @click="emit('cancel')">
            {{ $t("common.cancel") }}
          </Button>
          <div class="flex items-center gap-2">
            <Button
              v-if="step !== 'categories'"
              variant="outline"
              @click="
                emit(
                  'change-step',
                  step === 'review' ? 'products' : 'categories',
                )
              "
            >
              <ChevronLeft class="mr-1 size-4" />
              {{ $t("common.back") }}
            </Button>
            <Button v-if="step === 'categories'" @click="emit('continue')">
              {{
                $t("marketplace.connectionPage.products.publishWizardContinue")
              }}
              <ChevronRight class="ml-1 size-4" />
            </Button>
            <Button
              v-else-if="step === 'products'"
              :disabled="isChecking || !selectedCount"
              @click="emit('check')"
            >
              <Spinner v-if="isChecking" class="mr-2" />
              {{
                $t("marketplace.connectionPage.products.publishWizardCheck", {
                  count: selectedCount,
                })
              }}
              <ChevronRight v-if="!isChecking" class="ml-1 size-4" />
            </Button>
            <Button
              v-else
              :disabled="!readiness?.readyCount || isPublishing"
              @click="emit('publish')"
            >
              <Spinner v-if="isPublishing" class="mr-2" />
              {{
                $t("marketplace.connectionPage.products.publishConfirm", {
                  count: readiness?.readyCount || 0,
                })
              }}
            </Button>
          </div>
        </DialogFooter>
      </div>
    </DialogContent>
  </Dialog>
</template>
