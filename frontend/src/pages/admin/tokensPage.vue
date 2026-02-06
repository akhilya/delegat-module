<script>
import Page from "@/components/page.vue";
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

export default {
  components: {
    Page,
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

    <div class="flex justify-between items-center mb-4">
      <div>
        <h1 class="text-lg font-semibold">Токены приглашения</h1>
        <p class="text-sm text-muted-foreground">
          Управление токенами для регистрации пользователей
        </p>
      </div>

      <Button @click="handleCreateToken" :disabled="isCreating">
        <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
        <Plus v-else class="size-4 mr-2" />
        Создать токен
      </Button>
    </div>

    <!-- Last created token highlight -->
    <div
      v-if="lastCreatedToken"
      class="mb-4 p-4 bg-green-50 dark:bg-green-950 border border-green-200 dark:border-green-800 rounded-lg"
    >
      <p class="text-sm text-green-800 dark:text-green-200 mb-2 font-medium">
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

    <div class="border rounded-lg h-full w-full grow mb-4">
      <div v-if="isLoading" class="flex justify-center py-12">
        <Loader2 class="size-8 animate-spin text-muted-foreground" />
      </div>

      <div
        v-else-if="tokens.length === 0"
        class="flex flex-col items-center justify-center py-12 text-center"
      >
        <Key class="size-12 mb-4 text-muted-foreground" />
        <h3 class="font-medium mb-1">Нет токенов</h3>
        <p class="text-sm text-muted-foreground">Создайте первый токен приглашения</p>
      </div>

      <div v-else>
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
    </div>
  </Page>
</template>
