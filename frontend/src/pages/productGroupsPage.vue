<script>
import Page from "@/components/page.vue";
import PageSection from "@/components/pageSection.vue";
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
import { PageHeader } from "@/components/ui/page-header";
import { ConfirmDialog } from "@/components/ui/confirm-dialog";
import {
  Card,
} from "@/components/ui/card";

export default {
  components: {
    Page,
    PageSection,
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
    PageHeader,
    ConfirmDialog,
    Card,
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

    <PageHeader title="Группы товаров" description="Объединение товаров для синхронизации с адаптерами">
      <Dialog v-model:open="showCreateDialog">
        <DialogTrigger as-child>
          <Button>
            <Plus class="size-4 mr-2" />
            Создать группу
          </Button>
        </DialogTrigger>
        <DialogContent size="md">
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
    </PageHeader>

    <PageSection
      :loading="isLoading"
      :empty="groups.length === 0"
      :empty-icon="Layers"
      empty-title="Нет групп товаров"
      empty-description="Создайте первую группу для объединения товаров"
    >
      <template #empty-action>
        <Button @click="showCreateDialog = true">
          <Plus class="size-4 mr-2" />
          Создать группу
        </Button>
      </template>

      <Card class="p-0 gap-0 overflow-hidden">
        <ul class="divide-y divide-border/60">
          <li
            v-for="group in groups"
            :key="group.id"
            class="flex items-center gap-4 px-5 py-3.5 hover:bg-muted/40 cursor-pointer group transition-colors"
            @click="openGroup(group.id)"
          >
            <Layers class="size-5 text-muted-foreground shrink-0" />
            <div class="min-w-0 flex-1">
              <div class="font-medium truncate">{{ group.name }}</div>
              <div v-if="group.description" class="text-sm text-muted-foreground truncate">{{ group.description }}</div>
            </div>
            <span class="flex items-center gap-1.5 text-sm text-muted-foreground shrink-0 tabular-nums" title="Товаров в группе">
              <Package class="size-4" />
              {{ group.membersCount || 0 }}
            </span>
            <div v-if="group.marketplaceLinks && group.marketplaceLinks.length > 0" class="flex flex-wrap gap-1 shrink-0 max-w-[40%]">
              <Badge
                v-for="link in group.marketplaceLinks"
                :key="link.connectionId"
                :variant="getSyncStatusVariant(link.status)"
                class="text-[11px] font-normal"
              >
                {{ link.connectionName }}
              </Badge>
            </div>
            <ConfirmDialog
              title="Удалить группу?"
              :description="`Вы уверены что хотите удалить группу &quot;${group.name}&quot;? Это действие нельзя отменить. Связи с адаптерами также будут удалены.`"
              confirm-label="Удалить"
              :loading="isDeleting"
              @confirm="handleDeleteGroup(group.id)"
            >
              <template #trigger>
                <Button
                  variant="ghost"
                  size="icon"
                  class="opacity-0 group-hover:opacity-100 transition-opacity shrink-0"
                  @click.stop
                >
                  <Trash2 class="size-4 text-destructive" />
                </Button>
              </template>
            </ConfirmDialog>
            <ChevronRight class="size-4 text-muted-foreground/60 shrink-0" />
          </li>
        </ul>
      </Card>
    </PageSection>
  </Page>
</template>
