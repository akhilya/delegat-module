<script>
import Page from "@/components/page.vue";
import NavigationBar from "@/components/navigationBar.vue";
import Breadcrumbs from "@/components/breadcrumbs.vue";
import Button from "@/components/ui/button/Button.vue";
import Input from "@/components/ui/input/Input.vue";
import Badge from "@/components/ui/badge/Badge.vue";
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, Layers, Loader2, ChevronRight, Package, Trash2, RefreshCw, CheckCircle, XCircle, Clock } from "lucide-vue-next";
import { PAGE_PRODUCT_GROUP_CREATE, PAGE_PRODUCTS } from "@/router";
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
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
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
    Badge,
    Plus,
    Layers,
    Loader2,
    ChevronRight,
    Package,
    Trash2,
    RefreshCw,
    CheckCircle,
    XCircle,
    Clock,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    AlertDialog,
    AlertDialogAction,
    AlertDialogCancel,
    AlertDialogContent,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogTrigger,
    Card,
    CardContent,
    CardDescription,
    CardHeader,
    CardTitle,
  },
  data() {
    return {
      groups: [],
      isLoading: false,
      isCreating: false,
      isDeleting: false,
      newGroupName: "",
      newGroupDescription: "",
      showCreateDialog: false,
      groupToDelete: null,
      breadcrumbs: [{ label: "Группы товаров" }],
      PAGE_PRODUCTS,
    };
  },
  methods: {
    ...mapActions(["getAllGroups", "createGroup", "deleteGroup"]),

    async fetchGroups() {
      this.isLoading = true;
      await this.getAllGroups({
        onSuccess: (response) => {
          this.groups = response.data.value;
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    async handleCreateGroup() {
      if (!this.newGroupName.trim()) {
        displaySonnerError("Введите название группы");
        return;
      }

      this.isCreating = true;
      await this.createGroup({
        name: this.newGroupName.trim(),
        description: this.newGroupDescription.trim() || null,
        productIds: [],
        onSuccess: (response) => {
          displaySonnerSuccess("Группа создана");
          this.newGroupName = "";
          this.newGroupDescription = "";
          this.showCreateDialog = false;
          // Navigate to edit page
          this.$router.push(`/product-groups/${response.data.value.id}/edit`);
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    async handleDeleteGroup(groupId) {
      this.isDeleting = true;
      await this.deleteGroup({
        id: groupId,
        onSuccess: () => {
          displaySonnerSuccess("Группа удалена");
          this.fetchGroups();
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isDeleting = false;
      this.groupToDelete = null;
    },

    openGroup(groupId) {
      this.$router.push(`/product-groups/${groupId}/edit`);
    },

    getSyncStatusVariant(status) {
      switch (status) {
        case "Synced":
          return "default";
        case "Pending":
          return "secondary";
        case "Error":
          return "destructive";
        default:
          return "outline";
      }
    },

    getSyncStatusLabel(status) {
      switch (status) {
        case "Synced":
          return "Синхронизировано";
        case "Pending":
          return "Ожидает";
        case "Error":
          return "Ошибка";
        default:
          return status;
      }
    },
  },
  mounted() {
    this.fetchGroups();
  },
};
</script>

<template>
  <Page class="flex flex-col">
    <NavigationBar />
    <Breadcrumbs :items="breadcrumbs" />

    <div class="flex justify-between items-center mb-4">
      <div>
        <h1 class="text-lg font-semibold">Группы товаров</h1>
        <p class="text-sm text-muted-foreground">
          Объединение товаров для синхронизации с маркетплейсами
        </p>
      </div>

      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            Создать группу
          </Button>
        </DialogTrigger>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Новая группа</DialogTitle>
            <DialogDescription>
              Введите название и описание для новой группы товаров
            </DialogDescription>
          </DialogHeader>
          <div class="py-4 space-y-4">
            <Input
              v-model="newGroupName"
              placeholder="Название группы"
              @keyup.enter="handleCreateGroup"
            />
            <Input
              v-model="newGroupDescription"
              placeholder="Описание (необязательно)"
            />
          </div>
          <DialogFooter>
            <DialogClose as-child>
              <Button variant="outline">Отмена</Button>
            </DialogClose>
            <Button @click="handleCreateGroup" :disabled="isCreating">
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

      <div v-else-if="groups.length === 0" class="flex flex-col items-center justify-center py-12 text-center">
        <Layers class="size-12 mb-4 text-muted-foreground" />
        <h3 class="font-medium mb-1">Нет групп товаров</h3>
        <p class="text-sm text-muted-foreground">Создайте первую группу для объединения товаров</p>
      </div>

      <div v-else class="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <Card
          v-for="group in groups"
          :key="group.id"
          class="cursor-pointer hover:bg-accent/50 transition-colors group"
        >
          <CardHeader @click="openGroup(group.id)">
            <CardTitle class="flex items-center justify-between">
              <span class="flex items-center gap-2">
                <Layers class="size-5" />
                {{ group.name }}
              </span>
              <ChevronRight class="size-5 text-muted-foreground" />
            </CardTitle>
            <CardDescription v-if="group.description">
              {{ group.description }}
            </CardDescription>
          </CardHeader>
          <CardContent class="flex flex-col gap-3">
            <div class="flex items-center gap-4 text-sm text-muted-foreground">
              <span class="flex items-center gap-1">
                <Package class="size-4" />
                {{ group.membersCount || 0 }} товаров
              </span>
            </div>

            <!-- Sync statuses -->
            <div v-if="group.marketplaceLinks && group.marketplaceLinks.length > 0" class="flex flex-wrap gap-1">
              <Badge
                v-for="link in group.marketplaceLinks"
                :key="link.connectionId"
                :variant="getSyncStatusVariant(link.status)"
                class="text-xs"
              >
                {{ link.connectionName }}
              </Badge>
            </div>

            <div class="flex justify-end">
              <AlertDialog>
                <AlertDialogTrigger as-child>
                  <Button
                    variant="ghost"
                    size="icon"
                    class="opacity-0 group-hover:opacity-100 transition-opacity"
                    @click.stop
                  >
                    <Trash2 class="size-4 text-destructive" />
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>Удалить группу?</AlertDialogTitle>
                    <AlertDialogDescription>
                      Вы уверены что хотите удалить группу "{{ group.name }}"?
                      Это действие нельзя отменить. Связи с маркетплейсами также будут удалены.
                    </AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>Отмена</AlertDialogCancel>
                    <AlertDialogAction
                      @click="handleDeleteGroup(group.id)"
                      :disabled="isDeleting"
                    >
                      <Loader2 v-if="isDeleting" class="size-4 mr-2 animate-spin" />
                      Удалить
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  </Page>
</template>
