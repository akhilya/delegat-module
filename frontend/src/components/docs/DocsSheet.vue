<script setup>
import { computed, onMounted, watch } from "vue";
import { useI18n } from "vue-i18n";
import { useStore } from "vuex";
import { BookOpen, ExternalLink } from "lucide-vue-next";
import { Button } from "@/components/ui/button";
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
import { SHEET_WIDTH_DEFAULTS, SHEET_WIDTH_STORAGE } from "@/composables/useSheetResize";
import { getDocsPageUrl, getDocsSiteOrigin } from "@/utils/docsUrl";
import { renderDocsMarkdown } from "@/lib/docsMarkdown";

const store = useStore();
const { locale } = useI18n();

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

function onArticleClick(event) {
  const a = event.target.closest("a[data-docs-slug]");
  if (!a || !event.currentTarget.contains(a)) return;
  event.preventDefault();
  selectSlug(a.getAttribute("data-docs-slug") || "");
}
</script>

<template>
  <Sheet :open="panelOpen" @update:open="onOpenChange">
    <SheetContent
      side="right"
      inset
      resizable
      :show-close="false"
      class="gap-0 p-0"
      :default-width="SHEET_WIDTH_DEFAULTS.docs"
      :storage-key="SHEET_WIDTH_STORAGE.docs"
      :aria-label="t('components.docsSheet.title')"
    >
      <SheetHeader variant="bar">
        <div class="flex min-h-8 items-center gap-2">
          <BookOpen class="size-4 shrink-0 text-muted-foreground" />
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

        <div class="min-h-0 min-w-0 flex-1 overflow-y-auto px-8 py-7">
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
          <div
            v-else-if="page"
            class="docs-article"
            @click="onArticleClick"
            v-html="articleHtml"
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

<style scoped>
.docs-article {
  color: var(--foreground);
  font-size: 0.9375rem;
  line-height: 1.7;
  letter-spacing: -0.006em;
}

.docs-article :deep(h1) {
  font-size: 1.5rem;
  font-weight: 600;
  letter-spacing: -0.024em;
  line-height: 1.25;
  margin: 0 0 1.25rem;
}

.docs-article :deep(h2) {
  font-size: 1.15rem;
  font-weight: 600;
  letter-spacing: -0.02em;
  line-height: 1.3;
  margin: 2.25rem 0 0.85rem;
}

.docs-article :deep(h3) {
  font-size: 1rem;
  font-weight: 600;
  letter-spacing: -0.015em;
  margin: 1.75rem 0 0.7rem;
}

.docs-article :deep(p) {
  margin: 0 0 1.15rem;
}

.docs-article :deep(ul),
.docs-article :deep(ol) {
  margin: 0 0 1.15rem;
  padding-left: 1.4rem;
}

.docs-article :deep(li) {
  margin: 0.4rem 0;
}

.docs-article :deep(li) > p {
  margin-bottom: 0.4rem;
}

.docs-article :deep(a) {
  color: var(--muted-foreground);
  font-weight: 500;
  text-decoration: underline;
  text-decoration-color: color-mix(in oklch, var(--muted-foreground) 80%, transparent);
  text-decoration-thickness: 1px;
  text-underline-offset: 0.22em;
}

.docs-article :deep(a:hover) {
  color: var(--foreground);
  text-decoration-color: var(--foreground);
}

.docs-article :deep(img) {
  max-width: 100%;
  height: auto;
  border-radius: 0.5rem;
  margin: 0.75rem 0 1.5rem;
}

.docs-article :deep(hr) {
  border: 0;
  border-top: 1px solid var(--border);
  margin: 2rem 0;
}

.docs-article :deep(blockquote) {
  margin: 0 0 1.25rem;
  padding: 0.15rem 0 0.15rem 1rem;
  border-left: 2px solid var(--border);
  color: var(--muted-foreground);
}

.docs-article :deep(pre),
.docs-article :deep(code) {
  font-size: 0.8125rem;
}

.docs-article :deep(:not(pre) > code) {
  padding: 0.15em 0.4em;
  border-radius: 0.3rem;
  background: var(--muted);
}

.docs-article :deep(pre) {
  overflow-x: auto;
  border-radius: 0.5rem;
  padding: 1rem 1.15rem;
  background: var(--muted);
  margin: 0 0 1.35rem;
  line-height: 1.6;
}

.docs-article :deep(pre code) {
  padding: 0;
  background: none;
}

.docs-article :deep(table) {
  width: 100%;
  border-collapse: separate;
  border-spacing: 0;
  margin: 1.5rem 0 2rem;
  font-size: 0.875rem;
  line-height: 1.55;
  font-variant-numeric: tabular-nums;
}

.docs-article :deep(th),
.docs-article :deep(td) {
  padding: 0.75rem 1rem;
  text-align: left;
  vertical-align: top;
  border-bottom: 1px solid var(--border);
}

.docs-article :deep(th) {
  font-size: 0.75rem;
  font-weight: 500;
  letter-spacing: 0.01em;
  color: var(--muted-foreground);
  background: var(--muted);
}

.docs-article :deep(th:first-child),
.docs-article :deep(td:first-child) {
  padding-left: 1rem;
}

.docs-article :deep(tr:last-child td) {
  border-bottom: 0;
}

.docs-article :deep(thead th:first-child) {
  border-top-left-radius: 0.5rem;
}

.docs-article :deep(thead th:last-child) {
  border-top-right-radius: 0.5rem;
}
</style>
