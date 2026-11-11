<script>
import { FieldGroup } from "@/components/ui/field";
import { EmptyState } from "@/components/ui/empty-state";
import { isAttributeType } from "@/lib/productAttributeValidation";
import AttributeFieldControl from "@/components/products/AttributeFieldControl.vue";

export default {
  emits: ["files-uploaded"],
  components: {
    FieldGroup,
    EmptyState,
    AttributeFieldControl,
  },
  props: {
    attributes: {
      type: Array,
      required: true,
    },
    fieldPrefix: {
      type: String,
      default: "storeAttributes",
    },
    files: { type: Array, default: () => [] },
    productId: { type: Number, default: null },
    onFileUpload: { type: Function, default: null },
  },
  computed: {
    sortedAttributes() {
      return [...this.attributes]
        .filter((attr) => attr.showOnProductPage !== false)
        .sort((a, b) => a.sortOrder - b.sortOrder);
    },
  },
  methods: {
    isFullWidthField(attribute) {
      return isAttributeType(attribute.type, "text") && attribute.isMultiline;
    },
  },
};
</script>

<template>
  <FieldGroup class="gap-5">
    <div
      class="grid min-w-0 grid-cols-1 gap-5 md:grid-cols-[repeat(2,minmax(0,1fr))]"
    >
      <div
        v-for="attribute in sortedAttributes"
        :key="attribute.id"
        class="min-w-0 overflow-hidden"
        :class="isFullWidthField(attribute) ? 'md:col-span-2' : ''"
      >
        <AttributeFieldControl
          :attribute="attribute"
          :field-prefix="fieldPrefix"
          id-prefix="store-attr-"
          :files="files"
          :product-id="productId"
          :on-file-upload="onFileUpload"
          @files-uploaded="$emit('files-uploaded', $event)"
        />
      </div>
    </div>

    <EmptyState
      v-if="sortedAttributes.length === 0"
      size="sm"
      :title="$t('products.storeAttributeForm.emptyTitle')"
      :description="$t('products.storeAttributeForm.emptyDescription')"
      class="md:col-span-2 py-8"
    />
  </FieldGroup>
</template>
