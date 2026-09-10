<script setup>
import { computed } from "vue";
import { useStore } from "vuex";
import { BookOpen } from "lucide-vue-next";
import {
  InsetGroup,
  InsetGroupItem,
} from "@/components/ui/inset-group";

const props = defineProps({
  slug: { type: String, default: "" },
  title: { type: String, required: true },
  description: { type: String, default: "" },
});

const store = useStore();

const canOpen = computed(() => props.slug != null && String(props.slug).length > 0);

function open() {
  if (!canOpen.value) return;
  store.dispatch("docs/openSlug", props.slug);
}
</script>

<template>
  <InsetGroup v-if="canOpen">
    <InsetGroupItem show-chevron @click="open">
      <template #leading>
        <BookOpen class="size-4" />
      </template>
      <p class="truncate">{{ title }}</p>
      <p
        v-if="description"
        class="text-muted-foreground mt-0.5 text-xs font-normal"
      >
        {{ description }}
      </p>
    </InsetGroupItem>
  </InsetGroup>
</template>
