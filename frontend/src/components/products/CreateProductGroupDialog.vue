<script setup>
/**
 * Create a product group. Bar chrome + Field stack — same as create store.
 */
import { ref, watch } from "vue";
import { useStore } from "vuex";
import { t } from "@/i18n";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Spinner } from "@/components/ui/spinner";
import Input from "@/components/ui/input/Input.vue";
import Textarea from "@/components/ui/textarea/Textarea.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";

const FORM_ID = "create-product-group-form";

const open = defineModel("open", { type: Boolean, default: false });
const emit = defineEmits(["created"]);

const store = useStore();
const name = ref("");
const description = ref("");
const nameError = ref("");
const descriptionError = ref("");
const isCreating = ref(false);

watch(open, (isOpen) => {
  if (!isOpen) return;
  name.value = "";
  description.value = "";
  nameError.value = "";
  descriptionError.value = "";
});

function validate() {
  nameError.value = "";
  descriptionError.value = "";
  const nameIssue = fieldLimitIssue(name.value, FIELD_LIMITS.groupName);
  if (nameIssue === "empty") {
    nameError.value = t("pages.productGroupsPage.nameRequired");
  } else if (nameIssue === "tooLong") {
    nameError.value = t("common.tooLong", { max: FIELD_LIMITS.groupName });
  }
  const descIssue = fieldLimitIssue(description.value, FIELD_LIMITS.groupDescription, {
    required: false,
  });
  if (descIssue === "tooLong") {
    descriptionError.value = t("common.tooLong", { max: FIELD_LIMITS.groupDescription });
  }
  return !nameError.value && !descriptionError.value;
}

async function handleCreate() {
  if (isCreating.value || !validate()) return;
  isCreating.value = true;
  await store.dispatch("createGroup", {
    name: name.value.trim(),
    description: description.value.trim() || null,
    productIds: [],
    onSuccess: (response) => {
      const group = response?.data?.value ?? response?.data;
      displaySonnerSuccess(t("pages.productGroupsPage.created"));
      open.value = false;
      emit("created", group);
    },
    onError: (error) => {
      displaySonnerError(error);
    },
  });
  isCreating.value = false;
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent
      size="md"
      class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
    >
      <DialogHeader variant="bar">
        <DialogTitle>{{ $t("pages.productGroupsPage.newGroupTitle") }}</DialogTitle>
        <DialogDescription>
          {{ $t("pages.productGroupsPage.newGroupDescription") }}
        </DialogDescription>
      </DialogHeader>
      <form
        :id="FORM_ID"
        class="min-h-0 flex-1 overflow-y-auto px-6 py-5"
        @submit.prevent="handleCreate"
      >
        <FieldGroup class="gap-5">
          <SettingsFormField
            :label="$t('common.name')"
            label-for="create-group-name"
            :error="nameError"
            required
          >
            <Input
              id="create-group-name"
              v-model="name"
              :placeholder="$t('pages.productGroupsPage.namePlaceholder')"
              :maxlength="FIELD_LIMITS.groupName"
              :disabled="isCreating"
              autocomplete="off"
            />
          </SettingsFormField>
          <SettingsFormField
            :label="$t('common.description')"
            label-for="create-group-description"
            :error="descriptionError"
          >
            <Textarea
              id="create-group-description"
              v-model="description"
              :placeholder="$t('pages.productGroupsPage.descriptionPlaceholder')"
              class="min-h-24 resize-none"
              :maxlength="FIELD_LIMITS.groupDescription"
              :disabled="isCreating"
            />
          </SettingsFormField>
        </FieldGroup>
      </form>
      <DialogFooter variant="bar">
        <DialogClose as-child>
          <Button type="button" variant="outline">{{ $t("common.cancel") }}</Button>
        </DialogClose>
        <Button
          type="submit"
          :form="FORM_ID"
          :disabled="isCreating"
        >
          <Spinner v-if="isCreating" class="mr-2" />
          {{ $t("pages.productGroupsPage.createGroup") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
