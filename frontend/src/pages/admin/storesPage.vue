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

    <div class="flex justify-between items-center mb-4">
      <div>
        <h1 class="text-lg font-semibold">Магазины</h1>
        <p class="text-sm text-muted-foreground">
          Управление магазинами системы
        </p>
      </div>

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
    </div>

    <div class="border rounded-lg h-full w-full grow mb-4 p-4">
      <div v-if="isLoading" class="flex justify-center py-12">
        <Loader2 class="size-8 animate-spin text-muted-foreground" />
      </div>

      <div
        v-else-if="stores.length === 0"
        class="flex flex-col items-center justify-center py-12 text-center"
      >
        <Store class="size-12 mb-4 text-muted-foreground" />
        <h3 class="font-medium mb-1">Нет магазинов</h3>
        <p class="text-sm text-muted-foreground">Создайте первый магазин</p>
      </div>

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
