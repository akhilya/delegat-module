<script>
import AppPageChrome from "@/components/AppPageChrome.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import { Spinner } from "@/components/ui/spinner";
import { EmptyState } from "@/components/ui/empty-state";
import { InsetGroup, InsetGroupItem } from "@/components/ui/inset-group";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { LogCodeBlock, LogStatusBadge } from "@/components/logs";
import { displaySonnerError } from "@/store/sonnerHelper";
import { loadMe } from "@/main";
import { PAGE_ADMIN_API } from "@/router";
import { Play } from "lucide-vue-next";
import { RouterLink } from "vue-router";
import {
  findPublicApiTryEndpoint,
  normalizePublicApiTryPath,
  PUBLIC_API_TRY_CUSTOM_ID,
  PUBLIC_API_TRY_ENDPOINTS,
  publicApiV1UrlFromEnv,
  sendPublicApiTry,
} from "@/lib/publicApiTry";

// Platform Tools catalog: id `public-api-try`, mode `page`.

export default {
  components: {
    AppPageChrome,
    Button,
    Input,
    Spinner,
    EmptyState,
    InsetGroup,
    InsetGroupItem,
    FieldGroup,
    SettingsFormField,
    LogCodeBlock,
    LogStatusBadge,
    Play,
    RouterLink,
  },
  data() {
    return {
      loading: true,
      publicApiEnabled: true,
      apiKey: "",
      endpointId: "me",
      customPath: "/stores/1/products",
      sending: false,
      lastResult: null,
      PAGE_ADMIN_API,
      PUBLIC_API_TRY_CUSTOM_ID,
      PUBLIC_API_TRY_ENDPOINTS,
    };
  },
  computed: {
    isCustom() {
      return this.endpointId === PUBLIC_API_TRY_CUSTOM_ID;
    },
    resolvedPath() {
      if (this.isCustom) return normalizePublicApiTryPath(this.customPath);
      return findPublicApiTryEndpoint(this.endpointId).path;
    },
    requestUrl() {
      if (!this.resolvedPath) return "";
      return publicApiV1UrlFromEnv(
        this.resolvedPath,
        import.meta.env.VITE_API_SERVER,
      );
    },
  },
  async mounted() {
    try {
      const me = await loadMe();
      this.publicApiEnabled = me?.publicApiEnabled !== false;
    } catch {
      this.publicApiEnabled = true;
    } finally {
      this.loading = false;
    }
  },
  methods: {
    pickEndpoint(id) {
      this.endpointId = id;
    },
    endpointCaption(entry) {
      return this.$t(`pages.publicApiTry.endpoints.${entry.id}`);
    },
    async send() {
      if (!this.apiKey.trim()) {
        this.lastResult = { error: "key_required" };
        return;
      }
      if (!this.resolvedPath || !this.requestUrl) {
        this.lastResult = { error: "invalid_path" };
        return;
      }
      this.sending = true;
      this.lastResult = null;
      try {
        const result = await sendPublicApiTry({
          url: this.requestUrl,
          apiKey: this.apiKey,
        });
        this.lastResult = {
          ...result,
          method: "GET",
          path: this.requestUrl,
        };
      } catch (error) {
        displaySonnerError(error);
      } finally {
        this.sending = false;
      }
    },
  },
};
</script>

<template>
  <AppPageChrome>
    <EmptyState v-if="loading" loading class="min-h-0 flex-1" />

    <div v-else class="page-region-in flex w-full max-w-3xl flex-col">
      <p class="mb-6 text-sm text-muted-foreground">
        {{ $t("pages.publicApiTry.modelBody") }}
      </p>

      <EmptyState
        v-if="!publicApiEnabled"
        framed
        :title="$t('pages.publicApiTry.disabledTitle')"
        :description="$t('pages.publicApiTry.disabledDescription')"
      >
        <template #icon>
          <Play />
        </template>
      </EmptyState>

      <form v-else class="flex flex-col gap-6" @submit.prevent="send">
        <FieldGroup>
          <SettingsFormField
            :label="$t('pages.publicApiTry.keyLabel')"
            label-for="public-api-try-key"
            :description="$t('pages.publicApiTry.keyHint')"
          >
            <Input
              id="public-api-try-key"
              v-model="apiKey"
              type="password"
              autocomplete="off"
              spellcheck="false"
              class="font-mono"
              :placeholder="$t('pages.publicApiTry.keyPlaceholder')"
              :disabled="sending"
            />
          </SettingsFormField>
        </FieldGroup>

        <InsetGroup
          role="radiogroup"
          :aria-label="$t('pages.publicApiTry.endpointLabel')"
        >
          <InsetGroupItem
            v-for="entry in PUBLIC_API_TRY_ENDPOINTS"
            :key="entry.id"
            role="radio"
            :aria-checked="endpointId === entry.id"
            :selected="endpointId === entry.id"
            @click="pickEndpoint(entry.id)"
          >
            <p>{{ endpointCaption(entry) }}</p>
            <p
              class="mt-0.5 font-mono text-xs font-normal text-muted-foreground"
            >
              GET /api/v1{{ entry.path }}
            </p>
          </InsetGroupItem>
          <InsetGroupItem
            role="radio"
            :aria-checked="isCustom"
            :selected="isCustom"
            @click="pickEndpoint(PUBLIC_API_TRY_CUSTOM_ID)"
          >
            <p>{{ $t("pages.publicApiTry.customLabel") }}</p>
            <p class="mt-0.5 text-xs font-normal text-muted-foreground">
              {{ $t("pages.publicApiTry.customHint") }}
            </p>
          </InsetGroupItem>
        </InsetGroup>

        <SettingsFormField
          v-if="isCustom"
          :label="$t('pages.publicApiTry.pathLabel')"
          label-for="public-api-try-path"
        >
          <Input
            id="public-api-try-path"
            v-model="customPath"
            class="font-mono"
            spellcheck="false"
            autocomplete="off"
            :placeholder="$t('pages.publicApiTry.pathPlaceholder')"
            :disabled="sending"
          />
        </SettingsFormField>

        <div class="flex flex-wrap items-center gap-2">
          <Button type="submit" :disabled="sending">
            <Spinner v-if="sending" class="mr-2" />
            <Play v-else class="mr-2 size-4" />
            {{ $t("pages.publicApiTry.send") }}
          </Button>
          <Button variant="link" class="h-auto px-0 text-xs" as-child>
            <RouterLink :to="{ path: PAGE_ADMIN_API, query: { tab: 'logs' } }">
              {{ $t("pages.publicApiTry.openLogs") }}
            </RouterLink>
          </Button>
        </div>
      </form>

      <div v-if="lastResult" class="mt-8 flex flex-col gap-3">
        <p
          v-if="lastResult.error === 'key_required'"
          class="px-1 text-sm text-muted-foreground"
        >
          {{ $t("pages.publicApiTry.keyRequired") }}
        </p>
        <p
          v-else-if="lastResult.error === 'invalid_path'"
          class="px-1 text-sm text-muted-foreground"
        >
          {{ $t("pages.publicApiTry.invalidPath") }}
        </p>
        <template v-else>
          <div class="flex flex-wrap items-center gap-3 px-1">
            <code class="text-xs text-muted-foreground">
              {{ lastResult.method }} {{ lastResult.path }}
            </code>
            <LogStatusBadge :status-code="lastResult.status" />
            <span class="text-xs text-muted-foreground tabular-nums">
              {{
                $t("components.logs.durationMs", { ms: lastResult.durationMs })
              }}
            </span>
          </div>
          <LogCodeBlock
            json
            :label="$t('pages.publicApiTry.response')"
            :text="lastResult.body"
            :tone="lastResult.ok ? 'default' : 'destructive'"
          />
        </template>
      </div>
    </div>
  </AppPageChrome>
</template>
