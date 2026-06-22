<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
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

export default {
  components: {
    Page,
    PageSection,
    NavigationBar,
    Breadcrumbs,
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
      breadcrumbs: [{ label: "Токены приглашения" }],
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
          displaySonnerSuccess("Токен создан");
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
        displaySonnerSuccess("Токен скопирован");
        setTimeout(() => {
          this.copiedToken = null;
        }, 2000);
      } catch (err) {
        displaySonnerError("Не удалось скопировать");
      }
    },

    formatDate(dateString) {
      if (!dateString) return "-";
      return new Date(dateString).toLocaleString("ru-RU", {
        day: "2-digit",
        month: "2-digit",
        year: "numeric",
        hour: "2-digit",
        minute: "2-digit",
      });
    },
  },
  mounted() {
    this.fetchTokens();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader title="Токены приглашения" description="Управление токенами для регистрации пользователей">
      <Button @click="handleCreateToken" :disabled="isCreating">
        <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
        <Plus v-else class="size-4 mr-2" />
        Создать токен
      </Button>
    </PageHeader>

    <!-- Last created token highlight -->
    <div
      v-if="lastCreatedToken"
      class="mb-4 rounded-md bg-success-muted p-4"
    >
      <p class="text-sm text-success-foreground mb-2 font-medium">
        Новый токен создан:
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
      empty-title="Нет токенов"
      empty-description="Токены позволяют другим пользователям зарегистрироваться и присоединиться к магазину"
    >
      <template #empty-action>
        <Button @click="handleCreateToken" :disabled="isCreating">
          <Plus class="size-4 mr-2" />
          Создать токен
        </Button>
      </template>

      <div class="overflow-x-auto border-y">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Токен</TableHead>
              <TableHead>Статус</TableHead>
              <TableHead>Создан</TableHead>
              <TableHead>Использован</TableHead>
              <TableHead>Кем</TableHead>
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
                  Использован
                </Badge>
                <Badge v-else variant="default">
                  Активный
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
    </PageSection>
  </Page>
</template>
