<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import {
  InsetGroup,
  InsetGroupItem,
  InsetGroupLabel,
} from "@/components/ui/inset-group";
import { EmptyState } from "@/components/ui/empty-state";
import AdapterIcon from "@/components/marketplace/AdapterIcon.vue";
import { Cable, Search, Wand2 } from "lucide-vue-next";
import { RouterLink } from "vue-router";
import { http } from "@/main";
import { displaySonnerError, extractResponseData } from "@/store/sonnerHelper";
import { toolDescription, toolHref, toolTitle } from "@/lib/toolsCatalog";

const ICONS = { Cable, Search, Wand2 };

export default {
  components: {
    AppPageChrome,
    InsetGroup,
    InsetGroupItem,
    InsetGroupLabel,
    EmptyState,
    AdapterIcon,
    Cable,
    Search,
    Wand2,
    RouterLink,
  },
  data() {
    return {
      loading: true,
      tools: [],
      RouterLink,
    };
  },
  computed: {
    breadcrumbs() {
      return [{ label: this.$t("pages.toolsPage.breadcrumb") }];
    },
    platformTools() {
      return this.tools.filter((entry) => entry.source !== "adapter");
    },
    adapterTools() {
      return this.tools.filter((entry) => entry.source === "adapter");
    },
    showSectionLabels() {
      return this.platformTools.length > 0 && this.adapterTools.length > 0;
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
    async loadTools() {
      this.loading = true;
      try {
        const response = await http.get("/tools", { cache: false });
        const data = extractResponseData({ data: response.data }, []);
        this.tools = Array.isArray(data) ? data : [];
      } catch (error) {
        displaySonnerError(error);
        this.tools = [];
      } finally {
        this.loading = false;
      }
    },
  },
};
</script>

<template>
  <AppPageChrome :breadcrumb-items="breadcrumbs">
    <EmptyState
      v-if="loading"
      loading
      class="min-h-0 flex-1"
    />
    <EmptyState
      v-else-if="tools.length === 0"
      :title="$t('pages.toolsPage.emptyTitle')"
      :description="$t('pages.toolsPage.emptyDescription')"
    >
      <template #icon>
        <Wand2 />
      </template>
    </EmptyState>
    <div v-else class="flex w-full max-w-xl flex-col">
      <p class="mb-6 text-sm text-muted-foreground">
        {{ $t("pages.toolsPage.description") }}
      </p>
      <div class="flex flex-col gap-8">
        <section v-if="platformTools.length">
          <InsetGroupLabel v-if="showSectionLabels">
            {{ $t("pages.toolsPage.sectionPlatform") }}
          </InsetGroupLabel>
          <InsetGroup>
            <InsetGroupItem
              v-for="entry in platformTools"
              :key="entry.id"
              :as="RouterLink"
              :to="toolHref(entry)"
              show-chevron
            >
              <template #leading>
                <component :is="iconFor(entry)" class="size-4" />
              </template>
              <p class="truncate">{{ toolTitle(entry, $t) }}</p>
              <p class="text-muted-foreground mt-0.5 line-clamp-2 text-xs font-normal">
                {{ toolDescription(entry, $t) }}
              </p>
              <template v-if="needsConnection(entry)" #trailing>
                <span class="text-muted-foreground text-xs font-normal">
                  {{ $t("pages.toolsPage.needsConnection") }}
                </span>
              </template>
            </InsetGroupItem>
          </InsetGroup>
        </section>
        <section v-if="adapterTools.length">
          <InsetGroupLabel v-if="showSectionLabels">
            {{ $t("pages.toolsPage.sectionAdapters") }}
          </InsetGroupLabel>
          <InsetGroup>
            <InsetGroupItem
              v-for="entry in adapterTools"
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
              <p class="text-muted-foreground mt-0.5 line-clamp-2 text-xs font-normal">
                {{ toolDescription(entry, $t) }}
              </p>
              <template v-if="needsConnection(entry)" #trailing>
                <span class="text-muted-foreground text-xs font-normal">
                  {{ $t("pages.toolsPage.needsConnection") }}
                </span>
              </template>
            </InsetGroupItem>
          </InsetGroup>
        </section>
      </div>
    </div>
  </AppPageChrome>
</template>
