<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, Store, Users, Loader2, ChevronRight } from "lucide-vue-next";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import {
  Card,
} from "@/components/ui/card";
import { PageHeader } from "@/components/ui/page-header";
import { EmptyState } from "@/components/ui/empty-state";

export default {
  components: {
    Page,
    NavigationBar,
    Breadcrumbs,
    Button,
    Input,
    Plus,
    Store,
    Users,
    Loader2,
    ChevronRight,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    Card,
    PageHeader,
    EmptyState,
  },
  data() {
    return {
      stores: [],
      isLoading: false,
      isCreating: false,
      newStoreName: "",
      showCreateDialog: false,
      breadcrumbs: [{ label: "Магазины" }],
    };
  },
  methods: {
    ...mapActions(["getAllStores", "createStore"]),

    async fetchStores() {
      this.isLoading = true;
      await this.getAllStores({
        onSuccess: (response) => {
          this.stores = response.data.value.items;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async handleCreateStore() {
      if (!this.newStoreName.trim()) {
        displaySonnerError("Введите название магазина");
        return;
      }

      this.isCreating = true;
      await this.createStore({
        name: this.newStoreName.trim(),
        onSuccess: (response) => {
          displaySonnerSuccess("Магазин создан");
          this.newStoreName = "";
          this.showCreateDialog = false;
          this.fetchStores();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    openStore(storeId) {
      this.$router.push(`/admin/stores/${storeId}`);
    },
  },
  mounted() {
    this.fetchStores();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <PageHeader title="Магазины" description="Управление магазинами системы">
      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            Создать магазин
          </Button>
        </DialogTrigger>
          <DialogContent class="sm:max-w-md">
            <DialogHeader>
              <DialogTitle>Новый магазин</DialogTitle>
              <DialogDescription>
                Введите название для нового магазина
              </DialogDescription>
            </DialogHeader>
            <div class="py-4">
              <Input
                v-model="newStoreName"
                placeholder="Название магазина"
                @keyup.enter="handleCreateStore"
              />
            </div>
            <DialogFooter>
              <DialogClose as-child>
                <Button variant="outline">Отмена</Button>
              </DialogClose>
              <Button @click="handleCreateStore" :disabled="isCreating">
                <Loader2 v-if="isCreating" class="size-4 mr-2 animate-spin" />
                Создать
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
    </PageHeader>

    <div class="border rounded-lg h-full w-full grow mb-4 p-4">
      <EmptyState v-if="isLoading" loading />
      <EmptyState
        v-else-if="stores.length === 0"
        title="Нет магазинов"
        description="Магазин — это рабочее пространство для управления товарами и синхронизации с маркетплейсами"
      >
        <template #icon>
          <Store class="size-12 mb-4 text-muted-foreground" />
        </template>
        <Button @click="showCreateDialog = true">
          <Plus class="size-4 mr-2" />
          Создать магазин
        </Button>
      </EmptyState>

      <Card v-else class="p-0 gap-0 overflow-hidden">
        <ul class="divide-y divide-border/60">
          <li
            v-for="store in stores"
            :key="store.id"
            class="flex items-center gap-4 px-5 py-3.5 hover:bg-muted/40 cursor-pointer group transition-colors"
            @click="openStore(store.id)"
          >
            <Store class="size-5 text-muted-foreground shrink-0" />
            <div class="min-w-0 flex-1">
              <div class="font-medium truncate">{{ store.name }}</div>
              <div class="text-xs text-muted-foreground tabular-nums">ID {{ store.id }}</div>
            </div>
            <div class="flex items-center gap-1.5 text-sm text-muted-foreground shrink-0 tabular-nums">
              <Users class="size-4" />
              {{ store.membersCount || 0 }}
            </div>
            <ChevronRight class="size-4 text-muted-foreground/60 shrink-0" />
          </li>
        </ul>
      </Card>
    </div>
  </Page>
</template>
