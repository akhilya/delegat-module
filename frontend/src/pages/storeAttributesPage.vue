<script>
import { Spinner } from "@/components/ui/spinner";
import AppPageChrome from "@/components/AppPageChrome.vue";
import PageSection from "@/components/pageSection.vue";
import { Button } from "@/components/ui/button";
import Input from "@/components/ui/input/Input.vue";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { FieldGroup } from "@/components/ui/field";
import AttributeDefinitionList from "@/components/products/AttributeDefinitionList.vue";
import AttributeUnitSelect from "@/components/products/AttributeUnitSelect.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
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
import { mapActions } from "vuex";
import { displaySonnerError, displaySonnerSuccess } from "@/store/sonnerHelper";
import { Plus, Settings2 } from "lucide-vue-next";
import { PAGE_STORE_ATTRIBUTE_EDIT } from "@/router";
import { FIELD_LIMITS, fieldLimitIssue } from "@/lib/fieldLimits";
import {
  STORE_ATTRIBUTE_TYPES,
  STORE_ATTRIBUTE_TYPE_VALUES,
  generateAttributeKey,
  resolveStoreAttributeTypeValue,
  storeAttributeRowCaption,
} from "@/lib/storeAttributeUi";

export default {
  components: {
    Spinner,
    AppPageChrome,
    PageSection,
    Button,
    Input,
    SettingsFormField,
    FieldGroup,
    AttributeDefinitionList,
    AttributeUnitSelect,
    Select,
    SelectContent,
    SelectItem,
    SelectTrigger,
    SelectValue,
    Dialog,
    DialogContent,
    DialogDescription,
    DialogHeader,
    DialogTitle,
    DialogTrigger,
    DialogFooter,
    DialogClose,
    Plus,
  },
  data() {
    return {
      attributes: [],
      isLoading: false,
      isCreating: false,
      isReordering: false,
      showCreateDialog: false,
      newAttributeName: "",
      newAttributeKey: "",
      newAttributeType: 0,
      newAttributeUnitCode: "",
      FIELD_LIMITS,
      STORE_ATTRIBUTE_TYPE_VALUES,
      emptyListIcon: Settings2,
    };
  },
  computed: {
    createTypeLabel() {
      return this.getAttributeTypeLabel(this.newAttributeType);
    },
    isCreateNumberType() {
      return resolveStoreAttributeTypeValue(this.newAttributeType) === 1;
    },
  },
  methods: {
    ...mapActions([
      "getAllStoreAttributes",
      "createStoreAttribute",
      "reorderStoreAttributes",
    ]),

    getAttributeTypeLabel(type) {
      const i18nKey = STORE_ATTRIBUTE_TYPES[type]?.i18nKey;
      return i18nKey
        ? this.$t(`pages.storeAttributesPage.types.${i18nKey}`)
        : this.$t("pages.storeAttributesPage.types.unknown");
    },

    attributeRowCaption(attribute) {
      return storeAttributeRowCaption(attribute, {
        typeLabel: this.getAttributeTypeLabel(attribute.type),
        optionsWord: this.$t("pages.storeAttributesPage.optionsWord"),
      });
    },

    async fetchAttributes({ silent = false } = {}) {
      if (!silent) this.isLoading = true;
      await this.getAllStoreAttributes({
        summary: true,
        onSuccess: (response) => {
          this.attributes = response.data.value || [];
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isLoading = false;
    },

    onCreateNameInput() {
      this.newAttributeKey = generateAttributeKey(this.newAttributeName);
    },

    resetCreateDialog() {
      this.newAttributeName = "";
      this.newAttributeKey = "";
      this.newAttributeType = 0;
      this.newAttributeUnitCode = "";
    },

    openAttribute(attributeId) {
      this.$router.push(
        PAGE_STORE_ATTRIBUTE_EDIT.replace(":id", String(attributeId)),
      );
    },

    async handleCreateAttribute() {
      const nameIssue = fieldLimitIssue(
        this.newAttributeName,
        FIELD_LIMITS.attributeName,
      );
      if (nameIssue === "empty") {
        displaySonnerError(
          this.$t("pages.storeAttributesPage.validation.nameRequired"),
        );
        return;
      }
      if (nameIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeName }),
        );
        return;
      }
      const keyIssue = fieldLimitIssue(
        this.newAttributeKey,
        FIELD_LIMITS.attributeKey,
      );
      if (keyIssue === "empty") {
        displaySonnerError(
          this.$t("pages.storeAttributesPage.validation.keyRequired"),
        );
        return;
      }
      if (keyIssue === "tooLong") {
        displaySonnerError(
          this.$t("common.tooLong", { max: FIELD_LIMITS.attributeKey }),
        );
        return;
      }

      this.isCreating = true;
      await this.createStoreAttribute({
        attribute: {
          name: this.newAttributeName.trim(),
          key: this.newAttributeKey.trim(),
          type: resolveStoreAttributeTypeValue(this.newAttributeType),
          unitCode: this.isCreateNumberType
            ? this.newAttributeUnitCode.trim()
            : "",
          isRequired: false,
          isUnique: false,
          isMultiline: false,
          showOnProductPage: true,
          options: null,
          defaultValue: null,
          description: null,
          descriptionInTooltip: false,
        },
        onSuccess: (response) => {
          const id = response.data?.value?.id ?? response.data?.id;
          displaySonnerSuccess(
            this.$t("pages.storeAttributesPage.toast.added"),
          );
          this.resetCreateDialog();
          this.showCreateDialog = false;
          if (id != null) {
            this.openAttribute(id);
          } else {
            this.fetchAttributes();
          }
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isCreating = false;
    },

    async handleReorder() {
      this.isReordering = true;
      const attributeIds = this.attributes.map((attribute) => attribute.id);
      await this.reorderStoreAttributes({
        attributeIds,
        onSuccess: () => {
          displaySonnerSuccess(
            this.$t("pages.storeAttributesPage.toast.orderSaved"),
          );
        },
        onError: (error) => {
          displaySonnerError(error);
        },
      });
      this.isReordering = false;
    },
  },
  mounted() {
    this.fetchAttributes();
  },
};
</script>

<template>
  <AppPageChrome>
    <div class="flex w-full max-w-xl flex-col">
      <div class="mb-6 flex flex-wrap items-center justify-start gap-2">
        <Dialog v-model:open="showCreateDialog">
          <DialogTrigger as-child>
            <Button>
              <Plus class="size-4 mr-2" />
              {{ $t("pages.storeAttributesPage.header.addButton") }}
            </Button>
          </DialogTrigger>
          <DialogContent
            size="md"
            class="flex max-h-[88vh] flex-col gap-0 overflow-hidden p-0"
          >
            <DialogHeader variant="bar">
              <DialogTitle>{{
                $t("pages.storeAttributesPage.dialog.newTitle")
              }}</DialogTitle>
              <DialogDescription>
                {{ $t("pages.storeAttributesPage.dialog.description") }}
              </DialogDescription>
            </DialogHeader>
            <form
              id="new-store-attribute-form"
              class="min-h-0 flex-1 overflow-y-auto px-6 py-5"
              @submit.prevent="handleCreateAttribute"
            >
              <FieldGroup class="gap-5">
                <SettingsFormField
                  :label="$t('pages.storeAttributesPage.dialog.nameLabel')"
                  label-for="new-store-attr-name"
                >
                  <Input
                    id="new-store-attr-name"
                    v-model="newAttributeName"
                    :placeholder="
                      $t('pages.storeAttributesPage.dialog.namePlaceholder')
                    "
                    :maxlength="FIELD_LIMITS.attributeName"
                    @input="onCreateNameInput"
                  />
                </SettingsFormField>
                <SettingsFormField
                  :label="$t('pages.storeAttributesPage.dialog.keyLabel')"
                  label-for="new-store-attr-key"
                >
                  <template #description>{{
                    $t("pages.storeAttributesPage.dialog.keyDescription")
                  }}</template>
                  <Input
                    id="new-store-attr-key"
                    v-model="newAttributeKey"
                    placeholder="brand, country, sku"
                    class="font-mono text-sm"
                    :maxlength="FIELD_LIMITS.attributeKey"
                  />
                </SettingsFormField>
                <SettingsFormField
                  :label="$t('pages.storeAttributesPage.dialog.typeLabel')"
                  label-for="new-store-attr-type"
                >
                  <Select v-model="newAttributeType">
                    <SelectTrigger id="new-store-attr-type" class="w-full">
                      <SelectValue :placeholder="createTypeLabel" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem
                        v-for="typeValue in STORE_ATTRIBUTE_TYPE_VALUES"
                        :key="typeValue"
                        :value="typeValue"
                      >
                        {{ getAttributeTypeLabel(typeValue) }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </SettingsFormField>
                <SettingsFormField
                  v-if="isCreateNumberType"
                  :label="$t('pages.storeAttributesPage.dialog.unitLabel')"
                  label-for="new-store-attr-unit-code"
                >
                  <AttributeUnitSelect
                    id="new-store-attr-unit-code"
                    v-model="newAttributeUnitCode"
                    :placeholder="
                      $t('pages.storeAttributesPage.dialog.unitPlaceholder')
                    "
                  />
                  <template #description>
                    {{ $t("pages.storeAttributesPage.dialog.unitHint") }}
                  </template>
                </SettingsFormField>
              </FieldGroup>
            </form>
            <DialogFooter variant="bar">
              <DialogClose as-child>
                <Button type="button" variant="outline">{{
                  $t("common.cancel")
                }}</Button>
              </DialogClose>
              <Button
                type="submit"
                form="new-store-attribute-form"
                :disabled="isCreating"
              >
                <Spinner v-if="isCreating" class="mr-2" />
                {{ $t("common.create") }}
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>

      <PageSection
        :loading="isLoading"
        :empty="attributes.length === 0"
        :empty-icon="emptyListIcon"
        :empty-title="$t('pages.storeAttributesPage.empty.title')"
        :empty-description="$t('pages.storeAttributesPage.empty.description')"
      >
        <template #empty-action>
          <Button @click="showCreateDialog = true">
            <Plus class="size-4 mr-2" />
            {{ $t("pages.storeAttributesPage.header.addButton") }}
          </Button>
        </template>

        <AttributeDefinitionList
          v-model="attributes"
          endpoint="/store-attributes"
          :caption="attributeRowCaption"
          :disabled="isReordering"
          @open="openAttribute($event.id)"
          @reorder="handleReorder"
        />
      </PageSection>
    </div>
  </AppPageChrome>
</template>
