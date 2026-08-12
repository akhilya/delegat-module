<script setup>
/**
 * Share file/folder dialog — Catalog Workbench chrome + Field stacks.
 * Two-step flow: choose TTL → show link with copy.
 */
import { computed } from "vue";
import { Check, Copy } from "lucide-vue-next";
import { t } from "@/i18n";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import Input from "@/components/ui/input/Input.vue";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { FieldGroup } from "@/components/ui/field";
import SettingsFormField from "@/components/settings/SettingsFormField.vue";
import { Spinner } from "@/components/ui/spinner";

const props = defineProps({
  open: { type: Boolean, default: false },
  targetType: { type: String, default: "file" }, // 'file' | 'folder'
  itemName: { type: String, default: "" },
  allowPermanent: { type: Boolean, default: false },
  ttlHours: { type: String, default: "168" },
  shareUrl: { type: String, default: "" },
  creating: { type: Boolean, default: false },
  copied: { type: Boolean, default: false },
});

const emit = defineEmits([
  "update:open",
  "update:ttlHours",
  "create",
  "copy",
  "revoke",
]);

const title = computed(() =>
  props.targetType === "folder"
    ? t("products.productFileManager.shareFolderTitle")
    : t("products.productFileManager.shareFileTitle"),
);

const hasLink = computed(() => Boolean(props.shareUrl));

const canRevokePublic = computed(
  () =>
    props.allowPermanent
    && props.ttlHours === "permanent"
    && hasLink.value,
);

const expirationDescription = computed(() => {
  if (props.ttlHours === "permanent") {
    return t("products.productFileManager.expirationPermanentDescription");
  }
  return t("products.productFileManager.expirationDescription");
});

function onOpenChange(value) {
  emit("update:open", value);
}

function selectUrl(event) {
  event?.target?.select?.();
}
</script>

<template>
  <Dialog :open="open" @update:open="onOpenChange">
    <DialogContent size="md">
      <DialogHeader>
        <DialogTitle>{{ title }}</DialogTitle>
        <DialogDescription
          class="truncate"
          :title="itemName || undefined"
        >
          {{ itemName }}
        </DialogDescription>
      </DialogHeader>

      <FieldGroup v-if="!hasLink" class="gap-4">
        <SettingsFormField
          :label="t('products.productFileManager.expirationLabel')"
          label-for="share-link-ttl"
          :description="expirationDescription"
        >
          <Select
            :model-value="ttlHours"
            @update:model-value="emit('update:ttlHours', $event)"
          >
            <SelectTrigger id="share-link-ttl" class="w-full">
              <SelectValue
                :placeholder="t('products.productFileManager.expirationPlaceholder')"
              />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="1">
                {{ t("products.productFileManager.ttl1Hour") }}
              </SelectItem>
              <SelectItem value="24">
                {{ t("products.productFileManager.ttl1Day") }}
              </SelectItem>
              <SelectItem value="168">
                {{ t("products.productFileManager.ttl7Days") }}
              </SelectItem>
              <SelectItem value="720">
                {{ t("products.productFileManager.ttl30Days") }}
              </SelectItem>
              <SelectItem v-if="allowPermanent" value="permanent">
                {{ t("products.productFileManager.ttlPermanent") }}
              </SelectItem>
            </SelectContent>
          </Select>
        </SettingsFormField>
      </FieldGroup>

      <FieldGroup v-else class="gap-4">
        <SettingsFormField
          :label="t('products.productFileManager.shareLinkLabel')"
          label-for="share-link-url"
        >
          <div class="flex min-w-0 gap-2">
            <Input
              id="share-link-url"
              :model-value="shareUrl"
              readonly
              class="min-w-0 flex-1 font-mono text-xs tabular-nums"
              @click="selectUrl"
              @focus="selectUrl"
            />
            <Button
              type="button"
              variant="outline"
              size="icon"
              :aria-label="t('products.productFileManager.copyPublicLinkTitle')"
              @click="emit('copy')"
            >
              <Check v-if="copied" class="size-4 text-success" />
              <Copy v-else class="size-4" />
            </Button>
          </div>
        </SettingsFormField>
      </FieldGroup>

      <DialogFooter v-if="!hasLink">
        <Button type="button" variant="outline" @click="onOpenChange(false)">
          {{ t("common.cancel") }}
        </Button>
        <Button type="button" :disabled="creating" @click="emit('create')">
          <Spinner v-if="creating" class="mr-2" />
          {{ t("products.productFileManager.createLinkAction") }}
        </Button>
      </DialogFooter>
      <DialogFooter v-else class="sm:justify-between">
        <Button
          v-if="canRevokePublic"
          type="button"
          variant="destructive"
          :disabled="creating"
          @click="emit('revoke')"
        >
          <Spinner v-if="creating" class="mr-2" />
          {{ t("products.productFileManager.stopPublicAccessAction") }}
        </Button>
        <div v-else />
        <Button type="button" variant="outline" @click="onOpenChange(false)">
          {{ t("common.close") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
