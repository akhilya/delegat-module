<script>

import PageSection from "@/components/pageSection.vue";


import { Button } from "@/components/ui/button";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, Key, Copy, Check, Loader2, UserCheck } from "lucide-vue-next";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import Badge from "@/components/ui/badge/Badge.vue";
import { PageHeader } from "@/components/ui/page-header";
import { useStoreDateTime } from "@/composables/useStoreDateTime";

export default {
  setup() {
    const { formatDateTime: formatStoreDateTime, loadMemberships } = useStoreDateTime();
    loadMemberships();
    return { formatStoreDateTime };
  },
  components: {
    PageSection,
    Button,
    Plus,
    Key,
    Copy,
    Check,
    Loader2,
    UserCheck,
    Table,
    TableBody,
    TableCell,
    TableHead,
    TableHeader,
    TableRow,
    Badge,
    PageHeader,
  },
  data() {
    return {
      tokens: [],

      isLoading: false,
      isCreating: false,
      lastCreatedToken: null,
      copiedToken: null,
    };
  },
  methods: {
    ...mapActions(["getAllTokens", "createToken"]),

    async fetchTokens() {
      this.isLoading = true;
      await this.getAllTokens({
        onSuccess: (response) => {
          this.tokens = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async handleCreateToken() {
      this.isCreating = true;
      await this.createToken({
        onSuccess: (response) => {
          const newToken = response.data.value.token;
          this.lastCreatedToken = newToken;
          displaySonnerSuccess(this.$t("pages.tokensPage.tokenCreated"));
          this.fetchTokens();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    async copyToken(token) {
      try {
        await navigator.clipboard.writeText(token);
        this.copiedToken = token;
        displaySonnerSuccess(this.$t("pages.tokensPage.tokenCopied"));
        setTimeout(() => {
          this.copiedToken = null;
        }, 2000);
      } catch (err) {
        displaySonnerError(this.$t("common.copyFailed"));
      }
    },

    formatDate(dateString) {
      if (!dateString) return "-";
      const formatted = this.formatStoreDateTime(dateString);
      return formatted === "—" ? "-" : formatted;
    },
  },
  mounted() {
    this.fetchTokens();
  },
};
</script>

<template>
  <div>
    <PageHeader :title="$t('pages.tokensPage.title')" :description="$t('pages.tokensPage.description')">
      <Button @click="handleCreateToken" :disabled="isCreating">
        <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
        <Plus v-else class="size-4 mr-2" />
        {{ $t("pages.tokensPage.createToken") }}
      </Button>
    </PageHeader>

    <!-- Last created token highlight -->
    <div
      v-if="lastCreatedToken"
      class="mb-4 rounded-md bg-success-muted p-4"
    >
      <p class="text-sm text-success-foreground mb-2 font-medium">
        {{ $t("pages.tokensPage.newTokenCreated") }}
      </p>
      <div class="flex items-center gap-2">
        <code class="flex-1 p-2 bg-white dark:bg-gray-900 rounded border text-sm font-mono">
          {{ lastCreatedToken }}
        </code>
        <Button
          variant="outline"
          size="sm"
          @click="copyToken(lastCreatedToken)"
        >
          <Check v-if="copiedToken === lastCreatedToken" class="size-4" />
          <Copy v-else class="size-4" />
        </Button>
      </div>
    </div>

    <PageSection
      :loading="isLoading"
      :empty="tokens.length === 0"
      :empty-icon="Key"
      :empty-title="$t('pages.tokensPage.emptyTitle')"
      :empty-description="$t('pages.tokensPage.emptyDescription')"
    >
      <template #empty-action>
        <Button @click="handleCreateToken" :disabled="isCreating">
          <Plus class="size-4 mr-2" />
          {{ $t("pages.tokensPage.createToken") }}
        </Button>
      </template>

      <div class="rounded-lg border overflow-hidden">
      <div class="overflow-x-auto">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>{{ $t("pages.tokensPage.columnToken") }}</TableHead>
              <TableHead>{{ $t("pages.tokensPage.columnStatus") }}</TableHead>
              <TableHead>{{ $t("pages.tokensPage.columnCreated") }}</TableHead>
              <TableHead>{{ $t("pages.tokensPage.columnUsed") }}</TableHead>
              <TableHead>{{ $t("pages.tokensPage.columnUsedBy") }}</TableHead>
              <TableHead class="w-[50px]"></TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="token in tokens" :key="token.id">
              <TableCell>
                <code class="text-sm font-mono">
                  {{ token.token.substring(0, 8) }}...{{ token.token.substring(token.token.length - 4) }}
                </code>
              </TableCell>
              <TableCell>
                <Badge v-if="token.used" variant="secondary">
                  <UserCheck class="size-3 mr-1" />
                  {{ $t("pages.tokensPage.statusUsed") }}
                </Badge>
                <Badge v-else variant="default">
                  {{ $t("pages.tokensPage.statusActive") }}
                </Badge>
              </TableCell>
              <TableCell class="text-muted-foreground text-sm">
                {{ formatDate(token.createdAt) }}
              </TableCell>
              <TableCell class="text-muted-foreground text-sm">
                {{ formatDate(token.usedAt) }}
              </TableCell>
              <TableCell>
                {{ token.usedByUserName || "-" }}
              </TableCell>
              <TableCell>
                <Button
                  v-if="!token.used"
                  variant="ghost"
                  size="sm"
                  @click="copyToken(token.token)"
                >
                  <Check v-if="copiedToken === token.token" class="size-4" />
                  <Copy v-else class="size-4" />
                </Button>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
      </div>
    </PageSection>
  </div>
</template>
