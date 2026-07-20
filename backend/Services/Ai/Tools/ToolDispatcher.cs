using System.Text.Json;
using System.Text.RegularExpressions;
using Delegat.Services.Ai.Tools.Handlers;

namespace Delegat.Services.Ai.Tools;

/// <summary>
/// Routes AI tool names to the appropriate handler instance.
/// Replaces the switch statement that was previously inline in AiChatService.DispatchToolAsync.
/// </summary>
public class ToolDispatcher
{
    private readonly ProductToolHandlers _productHandlers;
    private readonly CategoryToolHandlers _categoryHandlers;
    private readonly MappingToolHandlers _mappingHandlers;
    private readonly GroupToolHandlers _groupHandlers;
    private readonly SyncToolHandlers _syncHandlers;
    private readonly FileToolHandlers _fileHandlers;
    private readonly AnalyticsToolHandlers _analyticsHandlers;
    private readonly RollbackToolHandlers _rollbackHandlers;
    private readonly ILogger<ToolDispatcher> _logger;

    public ToolDispatcher(
        ProductToolHandlers productHandlers,
        CategoryToolHandlers categoryHandlers,
        MappingToolHandlers mappingHandlers,
        GroupToolHandlers groupHandlers,
        SyncToolHandlers syncHandlers,
        FileToolHandlers fileHandlers,
        AnalyticsToolHandlers analyticsHandlers,
        RollbackToolHandlers rollbackHandlers,
        ILogger<ToolDispatcher> logger)
    {
        _productHandlers = productHandlers;
        _categoryHandlers = categoryHandlers;
        _mappingHandlers = mappingHandlers;
        _groupHandlers = groupHandlers;
        _syncHandlers = syncHandlers;
        _fileHandlers = fileHandlers;
        _analyticsHandlers = analyticsHandlers;
        _rollbackHandlers = rollbackHandlers;
        _logger = logger;
    }

    public async Task<(object result, string summary)> DispatchAsync(
        string toolName, string inputJson, int storeId, int userId, CancellationToken ct)
    {
        try
        {
            return toolName switch
            {
                // Products
                "list_products"              => await _productHandlers.ListProductsAsync(inputJson, storeId, userId, ct),
                "get_products_count"         => await _productHandlers.GetProductsCountAsync(inputJson, storeId, userId, ct),
                "get_product"                => await _productHandlers.GetProductAsync(inputJson, storeId, userId, ct),
                "create_product"             => await _productHandlers.CreateProductAsync(inputJson, storeId, userId, ct),
                "create_products"            => await _productHandlers.CreateProductsBulkAsync(inputJson, storeId, userId, ct),
                "update_product"             => await _productHandlers.UpdateProductAsync(inputJson, storeId, userId, ct),
                "update_product_sku"         => await _productHandlers.UpdateProductSkuAsync(inputJson, storeId, userId, ct),
                "update_product_prices"      => await _productHandlers.UpdateProductPricesAsync(inputJson, storeId, userId, ct),
                "update_product_attribute"   => await _productHandlers.UpdateProductAttributeAsync(inputJson, storeId, userId, ct),
                "update_product_attributes"  => await _productHandlers.UpdateProductAttributesBulkAsync(inputJson, storeId, userId, ct),
                "delete_products"            => await _productHandlers.DeleteProductsAsync(inputJson, storeId, userId, ct),
                "restore_products"           => await _productHandlers.RestoreProductsAsync(inputJson, storeId, userId, ct),
                "list_trash"                 => await _productHandlers.ListTrashAsync(inputJson, storeId, userId, ct),
                "generate_barcodes"          => await _productHandlers.GenerateBarcodesAsync(inputJson, storeId, userId, ct),

                // Categories
                "list_categories"            => await _categoryHandlers.ListCategoriesAsync(inputJson, storeId, userId, ct),
                "get_category"               => await _categoryHandlers.GetCategoryAsync(inputJson, storeId, userId, ct),
                "create_category"            => await _categoryHandlers.CreateCategoryAsync(inputJson, storeId, userId, ct),
                "update_category"            => await _categoryHandlers.UpdateCategoryAsync(inputJson, storeId, userId, ct),
                "delete_category"            => await _categoryHandlers.DeleteCategoryAsync(inputJson, storeId, userId, ct),
                "list_category_fields"       => await _categoryHandlers.ListCategoryFieldsAsync(inputJson, storeId, userId, ct),
                "add_category_field"         => await _categoryHandlers.AddCategoryFieldAsync(inputJson, storeId, userId, ct),
                "update_category_field"      => await _categoryHandlers.UpdateCategoryFieldAsync(inputJson, storeId, userId, ct),
                "delete_category_field"      => await _categoryHandlers.DeleteCategoryFieldAsync(inputJson, storeId, userId, ct),
                "find_categories_with_field" => await _categoryHandlers.FindCategoriesWithFieldAsync(inputJson, storeId, userId, ct),

                // Store attributes
                "list_store_attributes"      => await _analyticsHandlers.ListStoreAttributesAsync(inputJson, storeId, userId, ct),

                // Connections & sync
                "list_connections"           => await _syncHandlers.ListConnectionsAsync(inputJson, storeId, userId, ct),
                "get_connection"             => await _syncHandlers.GetConnectionAsync(inputJson, storeId, userId, ct),
                "test_connection"            => await _syncHandlers.TestConnectionAsync(inputJson, storeId, userId, ct),
                "get_connection_limits"      => await _syncHandlers.GetConnectionLimitsAsync(inputJson, storeId, userId, ct),
                "get_adapter_info"           => await _syncHandlers.GetAdapterInfoAsync(inputJson, storeId, userId, ct),
                "preview_marketplace_products" => await _syncHandlers.PreviewMarketplaceProductsAsync(inputJson, storeId, userId, ct),
                "import_products"            => await _syncHandlers.ImportProductsAsync(inputJson, storeId, userId, ct),
                "import_prices"              => await _syncHandlers.ImportPricesAsync(inputJson, storeId, userId, ct),
                "import_stocks"              => await _syncHandlers.ImportStocksAsync(inputJson, storeId, userId, ct),
                "archive_product_on_marketplace" => await _syncHandlers.ArchiveProductOnMarketplaceAsync(inputJson, storeId, userId, ct),
                "export_products_batch"      => await _syncHandlers.ExportProductsBatchAsync(inputJson, storeId, userId, ct),
                "export_prices"              => await _syncHandlers.ExportPricesAsync(inputJson, storeId, userId, ct),
                "export_stocks"              => await _syncHandlers.ExportStocksAsync(inputJson, storeId, userId, ct),
                "get_batch_status"           => await _syncHandlers.GetBatchStatusAsync(inputJson, storeId, userId, ct),
                "list_product_links"         => await _syncHandlers.ListProductLinksAsync(inputJson, storeId, userId, ct),

                // Mappings
                "list_mappings"              => await _mappingHandlers.ListMappingsAsync(inputJson, storeId, userId, ct),
                "get_mapping"                => await _mappingHandlers.GetMappingAsync(inputJson, storeId, userId, ct),
                "get_option_alignment_hints" => await _mappingHandlers.GetOptionAlignmentHintsAsync(inputJson, storeId, userId, ct),
                "create_mapping"             => await _mappingHandlers.CreateMappingAsync(inputJson, storeId, userId, ct),
                "update_mapping_name"        => await _mappingHandlers.UpdateMappingNameAsync(inputJson, storeId, userId, ct),
                "delete_mapping"             => await _mappingHandlers.DeleteMappingAsync(inputJson, storeId, userId, ct),
                "duplicate_mapping"          => await _mappingHandlers.DuplicateMappingAsync(inputJson, storeId, userId, ct),
                "list_external_categories"   => await _mappingHandlers.ListExternalCategoriesAsync(inputJson, storeId, userId, ct),
                "get_external_fields"        => await _mappingHandlers.GetExternalFieldsAsync(inputJson, storeId, userId, ct),
                "get_price_fields"           => await _mappingHandlers.GetPriceFieldsAsync(inputJson, storeId, userId, ct),
                "search_field_options"       => await _mappingHandlers.SearchFieldOptionsAsync(inputJson, storeId, userId, ct),
                "merge_adapter_fields"       => await _mappingHandlers.MergeAdapterFieldsAsync(inputJson, storeId, userId, ct),
                "unmerge_adapter_fields"     => await _mappingHandlers.UnmergeAdapterFieldsAsync(inputJson, storeId, userId, ct),
                "get_available_sources"      => await _mappingHandlers.GetAvailableSourcesAsync(inputJson, storeId, userId, ct),
                "get_mapping_coverage"       => await _mappingHandlers.GetMappingCoverageAsync(inputJson, storeId, userId, ct),
                "preview_mapping_for_product" => await _mappingHandlers.PreviewMappingAsync(inputJson, storeId, userId, ct),
                "connect_field"              => await _mappingHandlers.ConnectFieldAsync(inputJson, storeId, userId, ct),
                "connect_fields"             => await _mappingHandlers.ConnectFieldsBulkAsync(inputJson, storeId, userId, ct),
                "set_field_static_value"     => await _mappingHandlers.SetFieldStaticAsync(inputJson, storeId, userId, ct),
                "set_fields_static_value"    => await _mappingHandlers.SetFieldsStaticBulkAsync(inputJson, storeId, userId, ct),
                "add_field_modifier"         => await _mappingHandlers.AddFieldModifierAsync(inputJson, storeId, userId, ct),
                "remove_field_connection"    => await _mappingHandlers.RemoveFieldConnectionAsync(inputJson, storeId, userId, ct),
                "find_products_without_mapping" => await _mappingHandlers.FindProductsWithoutMappingAsync(inputJson, storeId, userId, ct),
                "get_product_mapping_summary" => await _mappingHandlers.GetProductMappingSummaryAsync(inputJson, storeId, userId, ct),
                "assign_product_mappings"    => await _mappingHandlers.AssignProductMappingsAsync(inputJson, storeId, userId, ct),

                // Groups
                "list_groups"                => await _groupHandlers.ListGroupsAsync(inputJson, storeId, userId, ct),
                "get_group"                  => await _groupHandlers.GetGroupAsync(inputJson, storeId, userId, ct),
                "create_group"               => await _groupHandlers.CreateGroupAsync(inputJson, storeId, userId, ct),
                "update_group"               => await _groupHandlers.UpdateGroupAsync(inputJson, storeId, userId, ct),
                "delete_group"               => await _groupHandlers.DeleteGroupAsync(inputJson, storeId, userId, ct),
                "add_products_to_group"      => await _groupHandlers.AddProductsToGroupAsync(inputJson, storeId, userId, ct),
                "remove_products_from_group" => await _groupHandlers.RemoveProductsFromGroupAsync(inputJson, storeId, userId, ct),

                // Files
                "list_files"                 => await _fileHandlers.ListFilesAsync(inputJson, storeId, userId, ct),
                "create_folder"              => await _fileHandlers.CreateFolderAsync(inputJson, storeId, userId, ct),
                "rename_file"                => await _fileHandlers.RenameFileAsync(inputJson, storeId, userId, ct),
                "rename_folder"              => await _fileHandlers.RenameFolderAsync(inputJson, storeId, userId, ct),
                "move_files"                 => await _fileHandlers.MoveFilesAsync(inputJson, storeId, userId, ct),
                "delete_files"               => await _fileHandlers.DeleteFilesAsync(inputJson, storeId, userId, ct),
                "delete_folder"              => await _fileHandlers.DeleteFolderAsync(inputJson, storeId, userId, ct),
                "share_file"                 => await _fileHandlers.ShareFileAsync(inputJson, storeId, userId, ct),

                // Analytics
                "get_store_stats"            => await _analyticsHandlers.GetStoreStatsAsync(inputJson, storeId, userId, ct),
                "find_products_without_images" => await _analyticsHandlers.FindProductsWithoutImagesAsync(inputJson, storeId, userId, ct),
                "find_products_without_barcode" => await _analyticsHandlers.FindProductsWithoutBarcodeAsync(inputJson, storeId, userId, ct),
                "find_products_missing_field" => await _analyticsHandlers.FindProductsMissingFieldAsync(inputJson, storeId, userId, ct),
                "find_products_with_sync_errors" => await _analyticsHandlers.FindProductsWithSyncErrorsAsync(inputJson, storeId, userId, ct),
                "analyze_mapping_quality"    => await _analyticsHandlers.AnalyzeMappingQualityAsync(inputJson, storeId, userId, ct),
                "get_image_download_status"  => await _analyticsHandlers.GetImageDownloadStatusAsync(inputJson, storeId, userId, ct),

                // Rollback
                "rollback_last_operation"    => await _rollbackHandlers.RollbackOperationAsync(inputJson, storeId, userId, ct),

                // Navigation (client-side only; backend validates URL safety)
                "navigate_to"                => NavigateTo(inputJson),

                _ => (new { error = $"Unknown tool: {toolName}" } as object, $"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {Tool} failed", toolName);
            return (new { error = ex.Message }, $"Error: {ex.Message}");
        }
    }

    private static readonly Regex _safePathRx =
        new(@"^/[a-zA-Z0-9\-/._]*$", RegexOptions.Compiled);

    private static (object, string) NavigateTo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var url   = doc.RootElement.GetProperty("url").GetString() ?? "";
        var label = doc.RootElement.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";

        if (!url.StartsWith('/')) url = "/" + url;
        if (url.Length > 200 || !_safePathRx.IsMatch(url))
            return (new { navigate = false, error = "Invalid URL" }, "Denied");

        return (new { navigate = true, url, label }, label);
    }
}
