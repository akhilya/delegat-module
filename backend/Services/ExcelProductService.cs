using ClosedXML.Excel;
using Delegat.Controllers;
using Delegat.Infrastructure.Data;
using Delegat.Models;
using Delegat.Models.Products;

namespace Delegat.Services;

public class ExcelProductService
{
    private static string SafeExcelText(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        // Defense-in-depth against spreadsheet formula injection when opened in Excel
        var c = value[0];
        if (c is '=' or '+' or '-' or '@' or '\t' or '\r')
            return "'" + value;
        return value;
    }

    public MemoryStream ExportToExcel(
        List<Product> products,
        List<CategoryAttribute> categoryAttributes,
        List<StoreAttribute> storeAttributes)
    {
        var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Products");

        // --- Fixed columns ---
        var fixedColumns = new (string key, string header, bool readOnly)[]
        {
            ("id", "ID", true),
            ("sku", "SKU", true),
            ("category", "Category", true),
            ("group", "Group", true),
            ("name", "Name", false),
            ("description", "Description", false),
            ("price", "Price", false),
            ("storageQuantity", "Stock qty", false),
            ("barcode", "Barcode", false),
        };

        int col = 1;

        // Row 1 (hidden): machine keys
        // Row 2: human-readable headers
        foreach (var (key, header, readOnly) in fixedColumns)
        {
            ws.Cell(1, col).Value = key;
            ws.Cell(2, col).Value = header;
            ws.Cell(2, col).Style.Font.Bold = true;
            ws.Cell(2, col).Style.Fill.BackgroundColor = readOnly
                ? XLColor.LightGray
                : XLColor.LightSteelBlue;
            col++;
        }

        // Dynamic: category attributes
        var catAttrList = categoryAttributes.OrderBy(a => a.categoryId).ThenBy(a => a.sortOrder).ToList();
        foreach (var attr in catAttrList)
        {
            ws.Cell(1, col).Value = $"catAttr:{attr.Id}";
            ws.Cell(2, col).Value = attr.name;
            ws.Cell(2, col).Style.Font.Bold = true;
            ws.Cell(2, col).Style.Fill.BackgroundColor = XLColor.LightGoldenrodYellow;
            col++;
        }

        // Dynamic: store attributes
        var storeAttrList = storeAttributes.OrderBy(a => a.sortOrder).ToList();
        foreach (var attr in storeAttrList)
        {
            ws.Cell(1, col).Value = $"storeAttr:{attr.Id}";
            ws.Cell(2, col).Value = attr.name;
            ws.Cell(2, col).Style.Font.Bold = true;
            ws.Cell(2, col).Style.Fill.BackgroundColor = XLColor.LightCyan;
            col++;
        }

        int totalCols = col - 1;

        // --- Data rows (starting at row 3) ---
        int row = 3;
        foreach (var p in products)
        {
            int c = 1;
            ws.Cell(row, c++).Value = p.Id;
            ws.Cell(row, c++).Value = SafeExcelText(p.sku);
            ws.Cell(row, c++).Value = SafeExcelText(p.category?.name ?? "");
            // Group(s) - comma-separated when product belongs to multiple groups.
            // Read-only: edits in this column are ignored on import.
            ws.Cell(row, c++).Value = SafeExcelText(string.Join(", ",
                p.groupMemberships?
                    .Where(gm => gm.group != null)
                    .Select(gm => gm.group.name)
                    .Distinct() ?? Enumerable.Empty<string>()));
            ws.Cell(row, c++).Value = SafeExcelText(p.name);
            ws.Cell(row, c++).Value = SafeExcelText(p.description);
            ws.Cell(row, c++).Value = p.price?.ToString() ?? "";
            ws.Cell(row, c++).Value = p.storageQuantity?.ToString() ?? "";
            ws.Cell(row, c++).Value = SafeExcelText(p.barcode ?? "");

            // Category attribute values
            foreach (var attr in catAttrList)
            {
                var val = p.attributeValues.FirstOrDefault(av => av.attributeId == attr.Id);
                ws.Cell(row, c++).Value = SafeExcelText(val?.value ?? "");
            }

            // Store attribute values
            foreach (var attr in storeAttrList)
            {
                var val = p.storeAttributeValues.FirstOrDefault(sav => sav.storeAttributeId == attr.Id);
                ws.Cell(row, c++).Value = SafeExcelText(val?.value ?? "");
            }

            // Gray background for read-only cells in data rows (id, sku, category, group)
            for (int i = 1; i <= 4; i++)
                ws.Cell(row, i).Style.Fill.BackgroundColor = XLColor.WhiteSmoke;

            row++;
        }

        // Hide row 1 (machine keys)
        ws.Row(1).Hide();

        // Freeze header row + ID/SKU columns
        ws.SheetView.FreezeRows(2);
        ws.SheetView.FreezeColumns(2);

        // Auto-fit column widths
        ws.Columns(1, totalCols).AdjustToContents(2, row - 1);

        // Cap max width at ~60 chars
        foreach (var c2 in ws.Columns(1, totalCols))
        {
            if (c2.Width > 60) c2.Width = 60;
        }

        var stream = new MemoryStream();
        wb.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    public ImportParseResult ParseImportFile(Stream file)
    {
        var wb = new XLWorkbook(file);
        var ws = wb.Worksheet(1);

        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        if (lastCol == 0 || lastRow < 3)
            return new ImportParseResult { Rows = new(), ColumnKeys = new() };

        // Read column keys from row 1
        var columnKeys = new List<string>();
        for (int c = 1; c <= lastCol; c++)
            columnKeys.Add(ws.Cell(1, c).GetString().Trim());

        // Find the ID column index
        int idColIdx = columnKeys.IndexOf("id");
        if (idColIdx < 0)
            return new ImportParseResult { Rows = new(), ColumnKeys = columnKeys };

        var rows = new List<ImportProductRow>();
        for (int r = 3; r <= lastRow; r++)
        {
            var idStr = ws.Cell(r, idColIdx + 1).GetString().Trim();
            if (string.IsNullOrEmpty(idStr) || !int.TryParse(idStr, out var productId))
                continue;

            var fields = new Dictionary<string, string?>();
            for (int c = 0; c < columnKeys.Count; c++)
            {
                var key = columnKeys[c];
                if (string.IsNullOrEmpty(key) || key == "id" || key == "sku" || key == "category" || key == "group")
                    continue;

                var cellValue = ws.Cell(r, c + 1).GetString();
                fields[key] = cellValue;
            }

            rows.Add(new ImportProductRow
            {
                ProductId = productId,
                RowNumber = r,
                Fields = fields
            });
        }

        return new ImportParseResult { Rows = rows, ColumnKeys = columnKeys };
    }

    public ImportChangeset BuildChangeset(ImportParseResult parsed, List<Product> currentProducts)
    {
        var changes = new List<ProductChange>();
        var errors = new List<ImportError>();

        var productMap = currentProducts.ToDictionary(p => p.Id);

        foreach (var row in parsed.Rows)
        {
            if (!productMap.TryGetValue(row.ProductId, out var product))
            {
                errors.Add(new ImportError
                {
                    Row = row.RowNumber,
                    ProductId = row.ProductId,
                    Field = "id",
                    Message = "Product not found"
                });
                continue;
            }

            var fieldChanges = new List<FieldChange>();

            foreach (var (key, newValue) in row.Fields)
            {
                string? oldValue = null;
                string fieldName = key;

                switch (key)
                {
                    case "name":
                        oldValue = product.name;
                        fieldName = "Name";
                        break;
                    case "description":
                        oldValue = product.description;
                        fieldName = "Description";
                        break;
                    case "price":
                        oldValue = product.price?.ToString() ?? "";
                        fieldName = "Price";
                        if (!string.IsNullOrEmpty(newValue) && !decimal.TryParse(newValue, out _))
                        {
                            errors.Add(new ImportError
                            {
                                Row = row.RowNumber,
                                ProductId = row.ProductId,
                                Field = key,
                                Message = $"Invalid price value: {newValue}"
                            });
                            continue;
                        }
                        break;
                    case "storageQuantity":
                        oldValue = product.storageQuantity?.ToString() ?? "";
                        fieldName = "Stock qty";
                        if (!string.IsNullOrEmpty(newValue) && !int.TryParse(newValue, out _))
                        {
                            errors.Add(new ImportError
                            {
                                Row = row.RowNumber,
                                ProductId = row.ProductId,
                                Field = key,
                                Message = $"Invalid quantity value: {newValue}"
                            });
                            continue;
                        }
                        break;
                    case "barcode":
                        oldValue = product.barcode ?? "";
                        fieldName = "Barcode";
                        if (!string.IsNullOrEmpty(newValue) && !ProductController.IsValidEanBarcode(newValue))
                        {
                            errors.Add(new ImportError
                            {
                                Row = row.RowNumber,
                                ProductId = row.ProductId,
                                Field = key,
                                Message = $"Invalid barcode: {newValue}"
                            });
                            continue;
                        }
                        break;
                    default:
                        if (key.StartsWith("catAttr:"))
                        {
                            var attrIdStr = key["catAttr:".Length..];
                            if (!int.TryParse(attrIdStr, out var attrId)) continue;
                            var existingVal = product.attributeValues
                                .FirstOrDefault(av => av.attributeId == attrId);
                            oldValue = existingVal?.value ?? "";
                            fieldName = existingVal?.attribute?.name ?? $"Category attribute #{attrId}";
                        }
                        else if (key.StartsWith("storeAttr:"))
                        {
                            var attrIdStr = key["storeAttr:".Length..];
                            if (!int.TryParse(attrIdStr, out var attrId)) continue;
                            var existingVal = product.storeAttributeValues
                                .FirstOrDefault(sav => sav.storeAttributeId == attrId);
                            oldValue = existingVal?.value ?? "";
                            fieldName = existingVal?.storeAttribute?.name ?? $"Store attribute #{attrId}";
                        }
                        else
                        {
                            continue; // Unknown column, skip
                        }
                        break;
                }

                var normalizedNew = newValue?.Trim() ?? "";
                var normalizedOld = oldValue?.Trim() ?? "";

                if (normalizedNew != normalizedOld)
                {
                    fieldChanges.Add(new FieldChange
                    {
                        Field = key,
                        FieldName = fieldName,
                        OldValue = normalizedOld,
                        NewValue = normalizedNew
                    });
                }
            }

            if (fieldChanges.Count > 0)
            {
                changes.Add(new ProductChange
                {
                    ProductId = product.Id,
                    ProductName = product.name,
                    ProductSku = product.sku,
                    Changes = fieldChanges
                });
            }
        }

        return new ImportChangeset
        {
            Changes = changes,
            Errors = errors,
            Summary = new ImportSummary
            {
                TotalRows = parsed.Rows.Count,
                ChangedProducts = changes.Count,
                UnchangedProducts = parsed.Rows.Count - changes.Count - errors.Count(e => e.Field == "id"),
                ErrorCount = errors.Count
            }
        };
    }

    public async Task<ImportApplyResult> ApplyChangeset(
        ImportChangeset changeset,
        List<Product> trackedProducts,
        IUnitOfWork unitOfWork)
    {
        var productMap = trackedProducts.ToDictionary(p => p.Id);
        var attrValueRepo = unitOfWork.Repository<ProductAttributeValue>();
        var storeAttrValueRepo = unitOfWork.Repository<StoreProductAttributeValue>();

        int appliedCount = 0;
        var applyErrors = new List<ImportError>();

        foreach (var productChange in changeset.Changes)
        {
            if (!productMap.TryGetValue(productChange.ProductId, out var product))
            {
                applyErrors.Add(new ImportError
                {
                    ProductId = productChange.ProductId,
                    Field = "id",
                    Message = "Product not found when applying"
                });
                continue;
            }

            // Track which category/store attrs are being set
            var catAttrUpdates = new Dictionary<int, string>();
            var storeAttrUpdates = new Dictionary<int, string>();

            foreach (var change in productChange.Changes)
            {
                switch (change.Field)
                {
                    case "name":
                        if (!string.IsNullOrEmpty(change.NewValue))
                            product.name = change.NewValue;
                        break;
                    case "description":
                        product.description = change.NewValue ?? "";
                        break;
                    case "price":
                        product.price = string.IsNullOrEmpty(change.NewValue)
                            ? null
                            : decimal.Parse(change.NewValue);
                        break;
                    case "storageQuantity":
                        product.storageQuantity = string.IsNullOrEmpty(change.NewValue)
                            ? null
                            : int.Parse(change.NewValue);
                        break;
                    case "barcode":
                        product.barcode = string.IsNullOrEmpty(change.NewValue) ? null : change.NewValue;
                        break;
                    default:
                        if (change.Field.StartsWith("catAttr:"))
                        {
                            var attrId = int.Parse(change.Field["catAttr:".Length..]);
                            catAttrUpdates[attrId] = change.NewValue ?? "";
                        }
                        else if (change.Field.StartsWith("storeAttr:"))
                        {
                            var attrId = int.Parse(change.Field["storeAttr:".Length..]);
                            storeAttrUpdates[attrId] = change.NewValue ?? "";
                        }
                        break;
                }
            }

            product.updatedAt = DateTime.UtcNow;

            // Apply category attribute changes
            if (catAttrUpdates.Count > 0)
            {
                foreach (var (attrId, newVal) in catAttrUpdates)
                {
                    var existing = product.attributeValues.FirstOrDefault(av => av.attributeId == attrId);
                    if (existing != null)
                    {
                        if (string.IsNullOrEmpty(newVal))
                        {
                            attrValueRepo.DeleteWithoutSave(existing);
                        }
                        else
                        {
                            existing.value = newVal;
                        }
                    }
                    else if (!string.IsNullOrEmpty(newVal))
                    {
                        var newAttrValue = new ProductAttributeValue();
                        newAttrValue.AddFill(new ProductAttributeValue.AddForm
                        {
                            productId = product.Id,
                            attributeId = attrId,
                            value = newVal
                        });
                        await attrValueRepo.AddWithoutSaveAsync(newAttrValue);
                    }
                }
            }

            // Apply store attribute changes
            if (storeAttrUpdates.Count > 0)
            {
                foreach (var (attrId, newVal) in storeAttrUpdates)
                {
                    var existing = product.storeAttributeValues.FirstOrDefault(sav => sav.storeAttributeId == attrId);
                    if (existing != null)
                    {
                        if (string.IsNullOrEmpty(newVal))
                        {
                            storeAttrValueRepo.DeleteWithoutSave(existing);
                        }
                        else
                        {
                            existing.value = newVal;
                        }
                    }
                    else if (!string.IsNullOrEmpty(newVal))
                    {
                        var newStoreAttrValue = new StoreProductAttributeValue();
                        newStoreAttrValue.AddFill(new StoreProductAttributeValue.AddForm
                        {
                            productId = product.Id,
                            storeAttributeId = attrId,
                            value = newVal
                        });
                        await storeAttrValueRepo.AddWithoutSaveAsync(newStoreAttrValue);
                    }
                }
            }

            appliedCount++;
        }

        await unitOfWork.SaveChangesAsync();

        return new ImportApplyResult
        {
            AppliedCount = appliedCount,
            Errors = applyErrors
        };
    }
}

// --- DTOs ---

public class ImportParseResult
{
    public List<ImportProductRow> Rows { get; set; } = new();
    public List<string> ColumnKeys { get; set; } = new();
}

public class ImportProductRow
{
    public int ProductId { get; set; }
    public int RowNumber { get; set; }
    public Dictionary<string, string?> Fields { get; set; } = new();
}

public class ImportChangeset
{
    public List<ProductChange> Changes { get; set; } = new();
    public List<ImportError> Errors { get; set; } = new();
    public ImportSummary Summary { get; set; } = new();
}

public class ProductChange
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string ProductSku { get; set; } = "";
    public List<FieldChange> Changes { get; set; } = new();
}

public class FieldChange
{
    public string Field { get; set; } = "";
    public string FieldName { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

public class ImportError
{
    public int Row { get; set; }
    public int? ProductId { get; set; }
    public string Field { get; set; } = "";
    public string Message { get; set; } = "";
}

public class ImportSummary
{
    public int TotalRows { get; set; }
    public int ChangedProducts { get; set; }
    public int UnchangedProducts { get; set; }
    public int ErrorCount { get; set; }
}

public class ImportApplyResult
{
    public int AppliedCount { get; set; }
    public List<ImportError> Errors { get; set; } = new();
}
