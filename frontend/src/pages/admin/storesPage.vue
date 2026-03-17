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
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
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
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
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
          <DialogContent>
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
        description="Создайте первый магазин"
      >
        <template #icon>
          <Store class="size-12 mb-4 text-muted-foreground" />
        </template>
        <Button @click="showCreateDialog = true">
          <Plus class="size-4 mr-2" />
          Создать магазин
        </Button>
      </EmptyState>

      <div v-else class="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <Card
          v-for="store in stores"
          :key="store.id"
          class="cursor-pointer hover:bg-accent/50 transition-colors"
          @click="openStore(store.id)"
        >
          <CardHeader>
            <CardTitle class="flex items-center justify-between">
              <span class="flex items-center gap-2">
                <Store class="size-5" />
                {{ store.name }}
              </span>
              <ChevronRight class="size-5 text-muted-foreground" />
            </CardTitle>
            <CardDescription>
              ID: {{ store.id }}
            </CardDescription>
          </CardHeader>
          <CardContent>
            <div class="flex items-center gap-2 text-sm text-muted-foreground">
              <Users class="size-4" />
              <span>{{ store.membersCount || 0 }} участников</span>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  </Page>
</template>
