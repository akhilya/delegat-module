<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import { EmptyState } from "@/components/ui/empty-state";
import { Spinner } from "@/components/ui/spinner";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import { Cable, DollarSign, Play, Search, Tag, Wand2 } from "lucide-vue-next";
import { RouterLink } from "vue-router";
import { http } from "@/main";
import {
  displaySonnerError,
  displaySonnerSuccess,
  extractResponseData,
} from "@/store/sonnerHelper";
import { getCachedMe } from "@/lib/meCache";
import { hasPermission, PERMS } from "@/lib/permissions";
import {
  adapterGroupLabel,
  groupToolsCatalog,
  toolConfirmDescriptionKey,
  toolConfirmTitleKey,
  toolDescription,
  toolHref,
  toolQuickTooltipKey,
  requiresSingleConnectionSelection,
  toolSectionBlocks,
  toolStageClass,
  toolStageLabelKey,
  toolTitle,
} from "@/lib/toolsCatalog";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";

const ICONS = { Cable, Search, Wand2, Play, DollarSign, Tag };

const SECTION_LABEL_KEYS = {
  catalog: "pages.toolsPage.sectionCatalog",
  prices: "pages.toolsPage.sectionPrices",
  promotions: "pages.toolsPage.sectionPromotions",
  api: "pages.toolsPage.sectionApi",
};

export default {
  components: {
    AppPageChrome,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    EmptyState,
    Spinner,
    AdapterIcon,
    Cable,
    DollarSign,
    Play,
    Search,
    Tag,
    Wand2,
    RouterLink,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
  },
  data() {
    return {
      loading: true,
      tools: [],
      adapters: [],
      RouterLink,
      confirmOpen: false,
      confirmEntry: null,
      runningId: null,
    };
  },
  computed: {
    canManageMarketplace() {
      return hasPermission(getCachedMe(), PERMS.MarketplaceManage);
    },
    catalogSections() {
      return groupToolsCatalog(this.tools);
    },
    confirmTitle() {
      const key = toolConfirmTitleKey(this.confirmEntry);
      return key ? this.$t(key) : "";
    },
    confirmDescription() {
      const key = toolConfirmDescriptionKey(this.confirmEntry);
      return key ? this.$t(key) : "";
    },
  },
  async mounted() {
    await this.loadTools();
  },
  methods: {
    toolTitle,
    toolDescription,
    toolHref,
    iconFor(entry) {
      return ICONS[entry.icon] || Wand2;
    },
    needsConnection(entry) {
      return entry.unavailableReason === "connection_required";
    },
    rowCaptions(entry) {
      const items = [];
      const stageKey = toolStageLabelKey(entry);
      if (stageKey) {
        items.push({
          key: "stage",
          text: this.$t(stageKey),
          className: toolStageClass(entry),
        });
      }
      if (this.needsConnection(entry)) {
        items.push({
          key: "connection",
          text: this.$t("pages.toolsPage.needsConnection"),
          className: "text-muted-foreground",
        });
      }
      return items;
    },
    canQuickRun(entry) {
      return (
        this.canManageMarketplace &&
        entry?.available !== false &&
        !requiresSingleConnectionSelection(entry) &&
        !this.needsConnection(entry)
      );
    },
    sectionBlocks(section) {
      return toolSectionBlocks(section?.tools);
    },
    runAria(entry) {
      const key = toolQuickTooltipKey(entry);
      return key ? this.$t(key) : this.$t("pages.toolsPage.quickRun");
    },
    sectionLabel(section) {
      if (section?.adapterKey) {
        const adapter = this.adapters.find(
          (item) => item.key === section.adapterKey,
        );
        return adapter?.displayName || adapterGroupLabel(section.adapterKey);
      }
      const key = SECTION_LABEL_KEYS[section?.id];
      return key ? this.$t(key) : this.$t("pages.toolsPage.sectionAdapters");
    },
    openQuickRun(entry) {
      if (!this.canQuickRun(entry)) return;
      this.confirmEntry = entry;
      this.confirmOpen = true;
    },
    async confirmQuickRun() {
      const entry = this.confirmEntry;
      if (!entry || !this.canQuickRun(entry)) return;
      this.runningId = entry.id;
      try {
        const response = await http.post(
          `/tools/${encodeURIComponent(entry.id)}/run`,
          { parameters: {} },
          { cache: false, timeout: 5 * 60 * 1000 },
        );
        const result = extractResponseData({ data: response.data }, {});
        const body =
          result?.value && typeof result.value === "object"
            ? result.value
            : result;
        this.$store.dispatch("operations/openPanel");
        displaySonnerSuccess(
          body.message || this.$t("pages.toolJob.runStarted"),
        );
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.runningId = null;
        this.confirmEntry = null;
      }
    },
    async loadTools() {
      this.loading = true;
      try {
        const [toolsResponse, adaptersResponse] = await Promise.all([
          http.get("/tools", { cache: false }),
          http.get("/marketplace/adapters", { cache: false }).catch(() => null),
        ]);
        const data = extractResponseData({ data: toolsResponse.data }, []);
        this.tools = Array.isArray(data) ? data : [];
        const adapters = adaptersResponse
          ? extractResponseData({ data: adaptersResponse.data }, [])
          : [];
        this.adapters = Array.isArray(adapters) ? adapters : [];
      } catch (error) {
        displaySonnerError(error);
        this.tools = [];
        this.adapters = [];
      } finally {
        this.loading = false;
      }
    },
  },
};
</script>

<template>
  <AppPageChrome>
    <EmptyState v-if="loading" loading class="min-h-0 flex-1" />
    <EmptyState
      v-else-if="tools.length === 0"
      :title="$t('pages.toolsPage.emptyTitle')"
      :description="$t('pages.toolsPage.emptyDescription')"
    >
      <template #icon>
        <Wand2 />
      </template>
    </EmptyState>
    <div v-else class="page-region-in flex w-full max-w-xl flex-col">
      <p class="mb-6 text-sm text-muted-foreground">
        {{ $t("pages.toolsPage.description") }}
      </p>
      <div class="flex flex-col gap-8">
        <section v-for="section in catalogSections" :key="section.id">
          <InsetGroupLabel>{{ sectionLabel(section) }}</InsetGroupLabel>
          <div class="flex flex-col gap-2">
            <template v-for="block in sectionBlocks(section)" :key="block.key">
              <InsetGroup v-if="block.type === 'list'">
                <InsetGroupItem
                  v-for="entry in block.tools"
                  :key="entry.id"
                  :as="RouterLink"
                  :to="toolHref(entry)"
                  show-chevron
                >
                  <template #leading>
                    <AdapterIcon
                      v-if="entry.adapterKey"
                      :adapter-key="entry.adapterKey"
                      class="size-4"
                    />
                    <component :is="iconFor(entry)" v-else class="size-4" />
                  </template>
                  <p class="truncate">{{ toolTitle(entry, $t) }}</p>
                  <p
                    class="text-muted-foreground mt-0.5 line-clamp-2 text-xs font-normal"
                  >
                    {{ toolDescription(entry, $t) }}
                  </p>
                  <template v-if="rowCaptions(entry).length" #trailing>
                    <span class="flex max-w-36 flex-col items-end gap-0.5">
                      <span
                        v-for="caption in rowCaptions(entry)"
                        :key="caption.key"
                        class="truncate text-xs font-normal"
                        :class="caption.className"
                      >
                        {{ caption.text }}
                      </span>
                    </span>
                  </template>
                </InsetGroupItem>
              </InsetGroup>
              <InsetGroup v-else>
                <InsetGroupItem
                  :as="RouterLink"
                  :to="toolHref(block.entry)"
                  show-chevron
                >
                  <template #leading>
                    <AdapterIcon
                      v-if="block.entry.adapterKey"
                      :adapter-key="block.entry.adapterKey"
                      class="size-4"
                    />
                    <component
                      :is="iconFor(block.entry)"
                      v-else
                      class="size-4"
                    />
                  </template>
                  <p class="truncate">{{ toolTitle(block.entry, $t) }}</p>
                  <p
                    class="text-muted-foreground mt-0.5 line-clamp-2 text-xs font-normal"
                  >
                    {{ toolDescription(block.entry, $t) }}
                  </p>
                  <template v-if="rowCaptions(block.entry).length" #trailing>
                    <span class="flex max-w-36 flex-col items-end gap-0.5">
                      <span
                        v-for="caption in rowCaptions(block.entry)"
                        :key="caption.key"
                        class="truncate text-xs font-normal"
                        :class="caption.className"
                      >
                        {{ caption.text }}
                      </span>
                    </span>
                  </template>
                </InsetGroupItem>
                <InsetGroupItem
                  :disabled="!canQuickRun(block.entry) || runningId != null"
                  :aria-label="runAria(block.entry)"
                  :class="
                    block.entry.destructive ? 'text-destructive' : undefined
                  "
                  @click="openQuickRun(block.entry)"
                >
                  <template #leading>
                    <Spinner v-if="runningId === block.entry.id" />
                    <Play v-else class="size-4" />
                  </template>
                  {{ $t("pages.toolsPage.quickRun") }}
                </InsetGroupItem>
              </InsetGroup>
            </template>
          </div>
        </section>
      </div>
    </div>

    <AlertDialog v-model:open="confirmOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{{ confirmTitle }}</AlertDialogTitle>
          <AlertDialogDescription>
            {{ confirmDescription }}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{{ $t("common.cancel") }}</AlertDialogCancel>
          <AlertDialogAction
            :variant="confirmEntry?.destructive ? 'destructive' : 'default'"
            :disabled="runningId != null"
            @click="confirmQuickRun"
          >
            <Spinner v-if="runningId" class="mr-2" />
            {{ $t("pages.toolsPage.quickRunConfirm") }}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </AppPageChrome>
</template>
