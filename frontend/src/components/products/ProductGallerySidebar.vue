<script setup>
import { Button } from "@/components/ui/button";
import { Loader2, Plus, X } from "lucide-vue-next";
import draggable from "vuedraggable";

const images = defineModel("images", { type: Array, required: true });

defineProps({
  isUploading: { type: Boolean, default: false },
  disabled: { type: Boolean, default: false },
  layout: {
    type: String,
    default: "sidebar",
    validator: (v) => ["sidebar", "inline"].includes(v),
  },
});

const emit = defineEmits(["drag-change", "remove", "upload", "preview"]);
</script>

<template>
  <div
    :class="[
      layout === 'sidebar'
        ? 'w-64 shrink-0 hidden lg:flex flex-col bg-card text-card-foreground border rounded-xl overflow-hidden sticky top-14 self-start h-[calc(100svh-3.5rem-1rem-4.75rem)]'
        : 'w-full flex flex-col gap-3',
    ]"
  >
    <div
      :class="[
        'shrink-0 border-b px-3 py-2',
        layout === 'inline' ? 'px-0 border-b-0 py-0' : '',
      ]"
    >
      <p class="text-sm font-medium">Фотографии</p>
      <p class="text-[11px] text-muted-foreground mt-0.5 leading-snug">
        Первое — главное
      </p>
    </div>

    <div
      :class="[
        layout === 'sidebar'
          ? 'flex-1 min-h-0 overflow-y-auto thin-scrollbar p-2'
          : '',
        disabled ? 'pointer-events-none opacity-50' : '',
      ]"
    >
      <draggable
        v-if="images?.length"
        :list="images"
        item-key="fileId"
        :animation="200"
        ghost-class="drag-ghost"
        chosen-class="drag-chosen"
        drag-class="drag-active"
        :class="[
          layout === 'sidebar'
            ? 'flex flex-col gap-1.5'
            : 'flex flex-wrap gap-2',
        ]"
        @change="emit('drag-change')"
      >
        <template #item="{ element, index }">
          <div
            :class="[
              'gallery-item group rounded-lg overflow-hidden bg-muted cursor-grab active:cursor-grabbing hover:ring-2 ring-primary relative',
              layout === 'sidebar' ? 'aspect-square w-full' : 'size-28 sm:size-32',
              index === 0 ? 'ring-2 ring-primary' : '',
            ]"
            @click="emit('preview', index)"
          >
            <img
              :src="element.previewUrl || `/api/files/${element.fileId}/thumbnail?w=256`"
              :alt="element.displayName || 'Фото товара'"
              :class="[
                'size-full pointer-events-none',
                layout === 'sidebar' ? 'object-cover' : 'object-contain',
              ]"
            />
            <div
              v-if="index === 0"
              class="absolute top-1.5 left-1.5 bg-primary text-primary-foreground text-[10px] px-1.5 py-0.5 rounded font-medium"
            >
              Главное
            </div>
            <Button
              type="button"
              variant="ghost"
              size="icon"
              class="absolute top-1.5 right-1.5 size-6 rounded-full bg-black/60 hover:bg-destructive text-white opacity-0 group-hover:opacity-100 transition-opacity"
              @click.stop="emit('remove', index)"
            >
              <X class="size-3" />
            </Button>
          </div>
        </template>
        <template #footer>
          <button
            type="button"
            :class="[
              'rounded-lg border border-dashed border-muted-foreground/30 hover:border-primary/50 flex items-center justify-center cursor-pointer transition-colors bg-transparent',
              layout === 'sidebar' ? 'aspect-square w-full' : 'size-28 sm:size-32',
            ]"
            @click="emit('upload')"
          >
            <Loader2 v-if="isUploading" class="size-5 text-muted-foreground animate-spin" />
            <Plus v-else class="size-5 text-muted-foreground" />
          </button>
        </template>
      </draggable>

      <button
        v-else
        type="button"
        :class="[
          'rounded-lg border border-dashed border-muted-foreground/30 hover:border-primary/50 flex items-center justify-center cursor-pointer transition-colors bg-transparent',
          layout === 'sidebar' ? 'aspect-square w-full' : 'size-28 sm:size-32',
        ]"
        @click="emit('upload')"
      >
        <Loader2 v-if="isUploading" class="size-5 text-muted-foreground animate-spin" />
        <Plus v-else class="size-5 text-muted-foreground" />
      </button>
    </div>
  </div>
</template>

<style>
.drag-ghost {
  opacity: 0.3;
  border: 2px dashed hsl(var(--primary));
  border-radius: 0.5rem;
}
.drag-chosen {
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15);
  border-radius: 0.5rem;
}
.drag-active {
  opacity: 1 !important;
  transform: scale(1.03) rotate(1deg);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.2);
  border-radius: 0.5rem;
  cursor: grabbing !important;
  z-index: 50;
}
</style>