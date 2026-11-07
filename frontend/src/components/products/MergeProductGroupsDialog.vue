<script setup>
import { computed, ref, watch } from "vue";
import { useStore } from "vuex";
import { t } from "@/i18n";
import { displaySonnerError, displaySonnerSuccess, extractResponseData } from "@/store/sonnerHelper";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter, DialogClose } from "@/components/ui/dialog";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";

const props = defineProps({ groups: { type: Array, default: () => [] } });
const open = defineModel("open", { type: Boolean, default: false });
const busy = defineModel("busy", { type: Boolean, default: false });
const emit = defineEmits(["merged"]);
const store = useStore();
const targetGroupId = ref("");
const target = computed(() => props.groups.find((g) => String(g.id) === targetGroupId.value));
const dialogOpen = computed({
  get: () => open.value,
  set: (value) => { if (!busy.value) open.value = value; },
});
watch(open, (value) => { if (value) targetGroupId.value = ""; });

async function merge() {
  if (busy.value || !target.value || props.groups.length < 2) return;
  const targetId = Number(target.value.id);
  const sourceGroupIds = props.groups.filter((g) => Number(g.id) !== targetId).map((g) => Number(g.id));
  busy.value = true;
  try {
    await store.dispatch("mergeProductGroups", {
      targetGroupId: targetId,
      sourceGroupIds,
      onSuccess: (response) => {
        const result = extractResponseData(response, {});
        displaySonnerSuccess(t("pages.productGroupsPage.mergeSuccess"));
        open.value = false;
        emit("merged", { ...result, sourceGroupIds });
      },
      onError: displaySonnerError,
    });
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <Dialog v-model:open="dialogOpen">
    <DialogContent
      size="md"
      :show-close="!busy"
      class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
      @escape-key-down="busy && $event.preventDefault()"
      @interact-outside="busy && $event.preventDefault()"
    >
      <DialogHeader variant="bar">
        <DialogTitle>{{ t("pages.productGroupsPage.mergeTitle") }}</DialogTitle>
        <DialogDescription>{{ t("pages.productGroupsPage.mergeDescription", { count: groups.length }) }}</DialogDescription>
      </DialogHeader>
      <form id="merge-product-groups" class="min-h-0 flex-1 space-y-5 overflow-y-auto px-6 py-5" @submit.prevent="merge">
        <FieldGroup>
        <SettingsFormField :label="t('pages.productGroupsPage.mergeTarget')" label-for="merge-group-target" required>
          <Select v-model="targetGroupId" :disabled="busy">
            <SelectTrigger id="merge-group-target" class="w-full">
              <SelectValue :placeholder="t('pages.productGroupsPage.mergeTargetPlaceholder')" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem v-for="group in groups" :key="group.id" :value="String(group.id)" :text-value="group.name" :title="group.name"><span class="min-w-0 truncate">{{ group.name }}</span></SelectItem>
            </SelectContent>
          </Select>
        </SettingsFormField>
        </FieldGroup>
        <p class="text-sm text-muted-foreground">{{ t("pages.productGroupsPage.mergeNotice") }}</p>
      </form>
      <DialogFooter variant="bar">
        <DialogClose as-child><Button type="button" variant="outline" :disabled="busy">{{ t("common.cancel") }}</Button></DialogClose>
        <Button type="submit" form="merge-product-groups" :disabled="busy || !target || groups.length < 2">
          <Spinner v-if="busy" class="mr-2" />{{ t("pages.productGroupsPage.mergeAction") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
