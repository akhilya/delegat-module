<script setup>
import { computed, onMounted, watch } from "vue";
import { useI18n } from "vue-i18n";
import { useStore } from "vuex";
import { BookOpen, ExternalLink, List } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetHeaderClose,
  SheetTitle,
} from "@/components/ui/sheet";
import { EmptyState } from "@/components/ui/empty-state";
import { t } from "@/i18n";
import { cn } from "@/lib/utils";
import { SHEET_WIDTH_DEFAULTS, SHEET_WIDTH_STORAGE, clampSheetWidth } from "@/composables/useSheetResize";
import { useCompanionSheets } from "@/composables/useCompanionSheets";
import { docsSheetUsesRail } from "@/lib/docsSheet";
import { getDocsPageUrl, getDocsSiteOrigin } from "@/utils/docsUrl";
import { renderDocsMarkdown } from "@/lib/docsMarkdown";
import { MarkdownProse } from "@/components/ui/markdown-prose";

const store = useStore();
const { locale } = useI18n();
const { edgeOffset } = useCompanionSheets();
const docsEdgeOffset = edgeOffset("docs");

const panelOpen = computed(() => store.state.docs.panelOpen);
const slug = computed(() => store.state.docs.slug);
const catalog = computed(() => store.state.docs.catalog);
const page = computed(() => store.state.docs.page);
const pageLoading = computed(() => store.state.docs.pageLoading);
const pageError = computed(() => store.state.docs.pageError);

const siteOrigin = computed(() => getDocsSiteOrigin());
const openOnSiteUrl = computed(() => getDocsPageUrl(slug.value, locale.value));
const title = computed(
  () => page.value?.title || t("components.docsSheet.title"),
);

const articleHtml = computed(() =>
  renderDocsMarkdown(page.value?.markdown || "", {
    currentSlug: slug.value,
    siteOrigin: siteOrigin.value,
  }),
);

onMounted(() => {
  store.dispatch("docs/ensureCatalog");
});

watch(locale, () => {
  store.dispatch("docs/ensureCatalog", { force: true });
  if (panelOpen.value) store.dispatch("docs/openSlug", slug.value);
});

function onOpenChange(open) {
  if (open) return;
  store.dispatch("docs/closePanel");
}

function selectSlug(next) {
  store.dispatch("docs/openSlug", next ?? "");
}

function selectTocSlug(next) {
  selectSlug(next === "_hub" ? "" : next);
}

const panelWidth = computed(() => store.state.docs.panelWidth);
const compactChrome = computed(
  () => !docsSheetUsesRail(clampSheetWidth(panelWidth.value)),
);
const tocValue = computed(() => slug.value || "_hub");

function onArticleClick(event) {
  const a = event.target.closest("a[data-docs-slug]");
  if (!a || !event.currentTarget.contains(a)) return;
  event.preventDefault();
  selectSlug(a.getAttribute("data-docs-slug") || "");
}
</script>

<template>
  <Sheet :open="panelOpen" :modal="false" @update:open="onOpenChange">
    <SheetContent
      side="right"
      inset
      resizable
      :overlay="false"
      :show-close="false"
      class="gap-0 p-0"
      :default-width="SHEET_WIDTH_DEFAULTS.docs"
      :storage-key="SHEET_WIDTH_STORAGE.docs"
      :width="store.state.docs.panelWidth"
      :edge-offset="docsEdgeOffset"
      :aria-label="t('components.docsSheet.title')"
      @update:width="(w) => store.dispatch('docs/setPanelWidth', w)"
    >
      <SheetHeader variant="bar">
        <div class="flex min-h-8 items-center gap-2">
          <DropdownMenu v-if="compactChrome">
            <DropdownMenuTrigger as-child>
              <Button
                data-slot="docs-toc-trigger"
                variant="ghost"
                size="icon"
                class="size-8 shrink-0"
                :title="t('components.docsSheet.catalog')"
                :aria-label="t('components.docsSheet.catalog')"
              >
                <List class="size-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent
              data-slot="docs-toc-menu"
              align="start"
              class="w-72"
            >
              <DropdownMenuRadioGroup
                :model-value="tocValue"
                @update:model-value="selectTocSlug"
              >
                <template
                  v-for="(group, gi) in catalog?.groups || []"
                  :key="gi"
                >
                  <DropdownMenuLabel>{{ group.text }}</DropdownMenuLabel>
                  <DropdownMenuRadioItem
                    v-for="(item, ii) in group.items || []"
                    :key="`${gi}-${ii}-${item.slug}`"
                    :value="item.slug || '_hub'"
                  >
                    {{ item.text }}
                  </DropdownMenuRadioItem>
                </template>
              </DropdownMenuRadioGroup>
            </DropdownMenuContent>
          </DropdownMenu>
          <BookOpen
            v-if="!compactChrome"
            class="size-4 shrink-0 text-muted-foreground"
          />
          <SheetTitle class="min-w-0 flex-1 truncate">
            {{ title }}
          </SheetTitle>
          <Button
            variant="ghost"
            size="icon"
            class="size-8 shrink-0"
            as="a"
            :href="openOnSiteUrl"
            target="_blank"
            rel="noopener noreferrer"
            :title="t('components.docsSheet.openOnSite')"
            :aria-label="t('components.docsSheet.openOnSite')"
          >
            <ExternalLink class="size-4" />
          </Button>
          <SheetHeaderClose />
        </div>
        <SheetDescription class="sr-only">
          {{ t("components.docsSheet.description") }}
        </SheetDescription>
      </SheetHeader>

      <div class="flex min-h-0 flex-1">
        <nav
          v-if="!compactChrome"
          data-slot="docs-toc"
          class="w-60 shrink-0 overflow-y-auto border-r border-border px-4 py-5"
          :aria-label="t('components.docsSheet.catalog')"
        >
          <section
            v-for="(group, gi) in catalog?.groups || []"
            :key="gi"
            class="mt-8 first:mt-0"
          >
            <p class="mb-2.5 px-2.5 text-xs font-medium tracking-wide text-muted-foreground">
              {{ group.text }}
            </p>
            <ul class="flex flex-col gap-0.5">
              <li v-for="(item, ii) in group.items || []" :key="`${gi}-${ii}-${item.slug}`">
                <button
                  type="button"
                  :data-selected="(item.slug || '') === slug ? 'true' : undefined"
                  :class="
                    cn(
                      'w-full rounded-md px-2.5 py-2 text-left text-sm leading-snug transition-colors duration-[var(--duration-ui)] ease-[var(--ease-out-quart)]',
                      (item.slug || '') === slug
                        ? 'bg-muted font-medium text-foreground'
                        : 'text-muted-foreground hover:bg-muted/60 hover:text-foreground',
                    )
                  "
                  @click="selectSlug(item.slug)"
                >
                  {{ item.text }}
                </button>
              </li>
            </ul>
          </section>
        </nav>

        <div
          :class="
            cn(
              'min-h-0 min-w-0 flex-1 overflow-y-auto',
              compactChrome ? 'px-5 py-6' : 'px-8 py-7',
            )
          "
        >
          <EmptyState
            v-if="pageLoading"
            loading
            :framed="false"
            :title="t('common.loading')"
          />
          <EmptyState
            v-else-if="pageError"
            :framed="false"
            :title="t('components.docsSheet.loadError')"
          />
          <MarkdownProse
            v-else-if="page"
            :html="articleHtml"
            @click="onArticleClick"
          />
          <EmptyState
            v-else
            :framed="false"
            :title="t('components.docsSheet.empty')"
          />
        </div>
      </div>
    </SheetContent>
  </Sheet>
</template>
