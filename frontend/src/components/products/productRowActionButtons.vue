<script>
// ProductRowActionButtons - Dropdown menu with actions for a single product table row.
import Button from "../ui/button/Button.vue";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { PAGE_PRODUCT_EDIT } from "@/router";
import { EllipsisVertical, Pencil, HardDrive, ExternalLink, StickyNote, Image, ArrowRightLeft } from "lucide-vue-next";

export default {
  components: {
    Button,
    DropdownMenu,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuTrigger,
    EllipsisVertical,
    Pencil,
    HardDrive,
    ExternalLink,
    StickyNote,
    Image,
    ArrowRightLeft,
  },
  props: {
    id: {
      type: Number,
      required: true,
    },
    name: {
      type: String,
      default: "",
    },
    sku: {
      type: String,
      default: "",
    },
  },
  emits: ["transfer"],
  methods: {
    navigate(tab) {
      this.$router.push({
        name: PAGE_PRODUCT_EDIT,
        params: { id: this.id },
        query: { tab },
      });
    },
    openTransfer() {
      this.$emit("transfer", { id: this.id, name: this.name, sku: this.sku });
    },
  },
};
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" class="w-full h-full rounded-none px-2 sm:px-3">
        <EllipsisVertical class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end">
      <DropdownMenuItem @click.stop="navigate('properties')">
        <Pencil class="size-4 mr-2" />
        Редактировать
      </DropdownMenuItem>
      <DropdownMenuItem @click.stop="navigate('files')">
        <HardDrive class="size-4 mr-2" />
        Файлы
      </DropdownMenuItem>
      <DropdownMenuItem @click.stop="navigate('images')">
        <Image class="size-4 mr-2" />
        Изображения
      </DropdownMenuItem>
      <DropdownMenuItem @click.stop="navigate('marketplaces')">
        <ExternalLink class="size-4 mr-2" />
        Адаптеры
      </DropdownMenuItem>
      <DropdownMenuItem @click.stop="navigate('notes')">
        <StickyNote class="size-4 mr-2" />
        Заметки
      </DropdownMenuItem>
      <DropdownMenuItem @click.stop="openTransfer">
        <ArrowRightLeft class="size-4 mr-2" />
        Перенести в магазин…
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
