import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { baseCompile } from "@intlify/message-compiler";
import en from "../locales/en.json" with { type: "json" };
import ru from "../locales/ru.json" with { type: "json" };
import { ATTRIBUTE_UNITS } from "./attributeUnits.js";

function walkStrings(value, prefix = "") {
  /** @type {Array<[string, string]>} */
  const out = [];
  if (typeof value === "string") {
    out.push([prefix, value]);
    return out;
  }
  if (Array.isArray(value)) {
    value.forEach((item, i) =>
      out.push(...walkStrings(item, `${prefix}[${i}]`)),
    );
    return out;
  }
  if (value && typeof value === "object") {
    for (const [k, v] of Object.entries(value)) {
      const key = prefix ? `${prefix}.${k}` : k;
      out.push(...walkStrings(v, key));
    }
  }
  return out;
}

function compileErrors(messages, locale) {
  const errors = [];
  for (const [key, source] of walkStrings(messages)) {
    const collected = [];
    baseCompile(source, {
      onError(err) {
        collected.push(err.message || String(err));
      },
    });
    for (const message of collected) {
      errors.push(`${locale} ${key}: ${message}\n  ${source}`);
    }
  }
  return errors;
}

function mentionsControlPlane(messages, locale) {
  return walkStrings(messages)
    .filter(([, source]) => /control plane/i.test(source))
    .map(([key, source]) => `${locale} ${key}: ${source}`);
}

describe("locale message catalogs", () => {
  it("compile without vue-i18n syntax errors", () => {
    const errors = [...compileErrors(ru, "ru"), ...compileErrors(en, "en")];
    assert.deepEqual(errors, []);
  });

  it("does not mention Control Plane in user-facing copy", () => {
    assert.deepEqual(
      [...mentionsControlPlane(ru, "ru"), ...mentionsControlPlane(en, "en")],
      [],
    );
  });

  it("localizes file attributes in the category editor", () => {
    for (const [locale, messages] of [
      ["en", en],
      ["ru", ru],
    ]) {
      const category = messages.pages.categoryEditPage;
      assert.equal(
        typeof category.attributeTypes.file,
        "string",
        `${locale}.pages.categoryEditPage.attributeTypes.file`,
      );
      assert.equal(
        typeof category.allowMultipleFilesLabel,
        "string",
        `${locale}.pages.categoryEditPage.allowMultipleFilesLabel`,
      );
      assert.equal(
        typeof category.allowMultipleFilesHint,
        "string",
        `${locale}.pages.categoryEditPage.allowMultipleFilesHint`,
      );
    }
  });

  it("localizes every known attribute unit and category unit field", () => {
    for (const [locale, messages] of [
      ["en", en],
      ["ru", ru],
    ]) {
      const unitMessages = messages.components.attributeUnitSelect.units;
      for (const unit of ATTRIBUTE_UNITS) {
        assert.equal(
          typeof unitMessages[unit.code],
          "string",
          `${locale}.components.attributeUnitSelect.units.${unit.code}`,
        );
        assert.ok(
          unitMessages[unit.code].trim(),
          `${locale}.components.attributeUnitSelect.units.${unit.code} is non-empty`,
        );
      }

      for (const key of ["pattern"]) {
        assert.equal(
          typeof messages.components.attributeFieldControl.constraints[key],
          "string",
          `${locale}.components.attributeFieldControl.constraints.${key}`,
        );
      }

      for (const key of [
        "uploadFile",
        "chooseFile",
        "chooseFiles",
        "selectedFileCount",
        "productFiles",
        "searchFiles",
        "noMatchingFiles",
        "fileResultsHint",
        "clearSelection",
      ]) {
        assert.equal(
          typeof messages.components.attributeFieldControl[key],
          "string",
          `${locale}.components.attributeFieldControl.${key}`,
        );
      }

      for (const key of ["unitLabel", "unitPlaceholder", "unitHint"]) {
        assert.equal(
          typeof messages.pages.categoryEditPage[key],
          "string",
          `${locale}.pages.categoryEditPage.${key}`,
        );
      }
    }
  });

  it("localizes the bulk attribute tooltip setting", () => {
    for (const [locale, messages] of [
      ["en", en],
      ["ru", ru],
    ]) {
      assert.equal(
        typeof messages.components.attributeDefinitions.descriptionInTooltip,
        "string",
        `${locale}.components.attributeDefinitions.descriptionInTooltip`,
      );
    }
  });

  it("localizes category creation and marketplace import choices", () => {
    const keys = [
      "addChoiceTitle",
      "addChoiceDescription",
      "createManually",
      "createManuallyDescription",
      "importFromMarketplace",
      "importFromMarketplaceDescription",
      "loadingMarketplaceConnections",
      "chooseImportConnectionTitle",
      "chooseImportConnectionDescription",
      "noImportConnectionsTitle",
      "noImportConnectionsDescription",
      "openMarketplaceConnections",
      "openMarketplaceConnectionsDescription",
    ];

    for (const [locale, messages] of [
      ["en", en],
      ["ru", ru],
    ]) {
      for (const key of keys) {
        assert.equal(
          typeof messages.pages.categoriesPage[key],
          "string",
          `${locale}.pages.categoriesPage.${key}`,
        );
        assert.ok(
          messages.pages.categoriesPage[key].trim(),
          `${locale}.pages.categoriesPage.${key} is non-empty`,
        );
      }
    }
  });

  it("localizes migrated API error codes in both supported languages", () => {
    const codes = [
      "categories_selection_invalid",
      "attributes_selection_invalid",
      "attributes_changes_required",
      "category_name_taken",
      "category_has_products",
      "attribute_name_taken",
      "attribute_options_not_available",
      "attribute_options_selection_invalid",
      "attribute_key_taken",
      "attribute_not_found",
      "attribute_unit_requires_number",
      "log_entry_not_found",
      "log_details_unavailable",
      "authenticator_key_failed",
      "adapter_not_found",
      "external_categories_unavailable",
      "mapping_not_found",
      "mapping_category_mismatch",
      "mapping_source_required",
      "mapping_modifier_required",
      "mapping_pairs_required",
      "mapping_replacement_invalid",
      "error_log_not_found",
      "refresh_token_missing",
      "plugin_file_required",
      "plugin_file_type_invalid",
      "plugin_file_too_large",
      "plugin_file_name_invalid",
      "system_plugin_protected",
      "encryption_not_configured",
      "tag_name_required",
      "tag_not_found",
      "folder_name_required",
      "folder_not_found",
      "target_folder_not_found",
      "folder_move_into_self",
      "folder_move_into_descendant",
      "invalid_ttl",
      "link_not_found",
      "file_name_required",
      "files_required",
      "groups_selection_invalid",
      "group_name_taken",
      "group_merge_selection_invalid",
      "group_merge_marketplace_conflict",
      "product_not_in_group",
      "note_not_found",
      "same_store_transfer",
      "plugins_disabled",
      "plugin_not_found",
      "plugin_load_failed",
      "plugin_unload_failed",
      "plugin_reload_failed",
      "external_fields_unavailable",
      "external_price_fields_unavailable",
      "mapping_type_invalid",
      "mapping_graph_invalid",
      "mapping_graph_conflict",
      "group_marketplace_sync_failed",
      "shipment_failed",
      "promotion_update_failed",
      "product_transfer_invalid",
      "transfer_mode_invalid",
      "single_store_move_forbidden",
      "target_store_not_found",
      "transfer_links_confirmation_required",
      "invalid_filter",
      "barcode_invalid",
      "barcode_already_used",
      "sku_required",
      "sku_too_long",
      "sku_unchanged",
      "products_required",
      "file_required",
      "store_not_found",
      "store_name_required",
      "store_name_taken",
      "timezone_invalid",
      "currency_code_invalid",
      "currency_rate_invalid",
      "currency_already_added",
      "currency_is_base",
      "base_currency_immutable",
      "currency_not_found",
      "user_already_store_member",
      "user_not_store_member",
      "store_members_manage_forbidden",
      "request_body_required",
      "store_attribute_not_found",
      "store_attribute_key_taken",
      "thumbnail_source_not_image",
      "thumbnail_failed",
      "username_required",
      "username_too_long",
      "password_required",
      "current_password_required",
      "owner_flag_change_forbidden",
      "owner_self_demote_forbidden",
      "last_owner_required",
      "self_deactivation_forbidden",
      "last_active_owner_required",
      "thumbnail_size_invalid",
      "public_link_root_forbidden",
      "token_already_used",
      "token_not_found",
      "shipment_not_found",
      "promotion_not_found",
      "order_action_required",
      "match_rows_required",
      "public_product_ids_required",
      "base_currency_unconfigured",
      "currency_unavailable",
      "currency_code_required",
      "file_too_large",
      "branding_asset_failed",
      "adapter_required",
      "product_fields_required",
      "api_key_name_required",
      "api_key_name_too_long",
      "api_key_not_found",
      "product_id_required",
      "product_and_mapping_required",
      "product_has_no_category",
      "price_delta_conflict",
      "marketplace_link_not_found",
      "store_attribute_key_used",
      "operation_not_found",
      "group_member_not_found",
      "variant_not_found",
      "import_nothing_queued",
      "invalid_color",
      "store_required",
      "multipart_content_type_invalid",
      "multipart_boundary_missing",
      "product_metadata_required",
      "product_metadata_duplicate",
      "product_metadata_too_large",
      "product_metadata_invalid",
      "upload_file_reference_missing",
      "invalid_uploaded_file_reference",
      "duplicate_uploaded_file_reference",
      "unreferenced_uploaded_file",
      "too_many_uploaded_files",
      "too_many_gallery_media_items",
      "unsupported_media_file",
      "duplicate_media_files",
      "required_attribute_file",
      "invalid_attribute_file_value",
      "conflicting_attribute_file_values",
      "attribute_multiple_files_disabled",
      "duplicate_attribute_files",
      "too_many_attribute_files",
      "file_ids_require_file_attribute",
      "file_in_use",
      "image_file_required",
      "product_name_required",
      "product_name_too_long",
      "multiple_files_requires_file_attribute",
      "file_attribute_cannot_be_unique",
      "file_attribute_default_unsupported",
      "attribute_type_change_with_values",
      "attribute_multiple_files_in_use",
      "files_not_supported_on_product_create",
      "storage_upload_failed",
      "media_export_video_limit_exceeded",
      "media_export_public_url_required",
      "media_export_public_url_invalid",
      "media_export_public_url_not_public",
      "media_export_connection_mismatch",
      "media_export_file_not_found",
      "media_export_file_not_video",
      "media_export_video_type_not_supported",
      "media_export_video_size_exceeded",
      "media_export_grants_unavailable",
      "cannot_change_own_access",
      "owner_role_required",
      "owner_role_assignment_forbidden",
      "tool_not_supported",
      "category_not_mappable",
      "plan_not_pending_or_mismatched",
      "hosted_ai_quota_exceeded",
      "hosted_ai_usage_unavailable",
      "hosted_ai_not_entitled",
      "hosted_ai_unavailable",
      "hosted_ai_provider_unavailable",
      "hosted_ai_request_too_large",
    ];

    for (const code of codes) {
      assert.equal(typeof en.errors[code], "string", `en.errors.${code}`);
      assert.equal(typeof ru.errors[code], "string", `ru.errors.${code}`);
    }
  });
});
