using System.IO.Compression;
using System.Xml;
using ClosedXML.Excel;
using Delegat.Controllers;
using Delegat.Infrastructure.Data;
using Delegat.Infrastructure.Middleware;
using Delegat.Models;
using Delegat.Models.Products;

namespace Delegat.Services;

public class ExcelProductService
{
    public const long MaxImportUploadBytes = 20L * 1024 * 1024;
    public const long MaxImportRequestBytes = MaxImportUploadBytes + 1024 * 1024;
    public const int MaxImportArchiveEntries = 1_000;
    public const long MaxImportExpandedBytes = 100L * 1024 * 1024;
    public const int MaxImportXmlElements = 5_000_000;
    public const int MaxImportCells = 1_000_000;
    public const int MaxImportDataRows = 50_000;
    public const int MaxImportColumns = 1_000;
    private const int ImportHeaderRows = 2;

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
        using var content = BufferImportUpload(file);
        EnsureImportArchiveWithinLimits(content);
        content.Position = 0;

        XLWorkbook wb;
        try
        {
            wb = new XLWorkbook(content);
        }
        catch (Exception ex) when (ex is not ApiException and not OutOfMemoryException)
        {
            throw ImportInvalid(ex);
        }

        using (wb)
            return ParseImportWorkbook(wb);
    }

    private static ImportParseResult ParseImportWorkbook(XLWorkbook wb)
    {
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws == null)
            return new ImportParseResult { Rows = new(), ColumnKeys = new() };

        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        if (lastCol > MaxImportColumns || lastRow > MaxImportDataRows + ImportHeaderRows)
            throw ImportTooLarge();

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

    private static MemoryStream BufferImportUpload(Stream file)
    {
        if (file.CanSeek && file.Length - file.Position > MaxImportUploadBytes)
            throw ImportTooLarge();

        var buffered = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (buffered.Length + read > MaxImportUploadBytes)
            {
                buffered.Dispose();
                throw ImportTooLarge();
            }
            buffered.Write(buffer, 0, read);
        }
        buffered.Position = 0;
        return buffered;
    }

    /// <summary>
    /// Inflates every archive entry through a byte budget before ClosedXML loads the workbook,
    /// so a small upload cannot expand into an unbounded in-memory workbook (zip bomb).
    /// Counts actual decompressed bytes rather than trusting declared entry sizes.
    /// </summary>
    private static void EnsureImportArchiveWithinLimits(Stream content)
    {
        content.Position = 0;
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException ex)
        {
            throw ImportInvalid(ex);
        }

        using (archive)
        {
            if (archive.Entries.Count > MaxImportArchiveEntries)
                throw ImportTooLarge();

            long declaredBytes = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.Length < 0 || entry.Length > MaxImportExpandedBytes - declaredBytes)
                    throw ImportTooLarge();
                declaredBytes += entry.Length;
            }

            var budget = new ImportExpansionBudget();
            foreach (var entry in archive.Entries)
            {
                try
                {
                    using var raw = entry.Open();
                    using var bounded = new BudgetedReadStream(raw, budget);
                    if (entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                    {
                        var isWorksheet = entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase);
                        CountImportXml(bounded, isWorksheet, budget);
                    }
                    else
                    {
                        bounded.CopyTo(Stream.Null);
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or XmlException)
                {
                    throw ImportInvalid(ex);
                }
            }
        }
    }

    private static void CountImportXml(Stream xml, bool isWorksheet, ImportExpansionBudget budget)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            CloseInput = false,
        };
        using var reader = XmlReader.Create(xml, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;
            if (++budget.Elements > MaxImportXmlElements)
                throw ImportTooLarge();
            if (isWorksheet && reader.LocalName == "c" && ++budget.Cells > MaxImportCells)
                throw ImportTooLarge();
        }
    }

    private static ApiException ImportTooLarge() => ApiException.BadRequest(
        $"The spreadsheet is too large to import (limits: {MaxImportUploadBytes / (1024 * 1024)} MB upload, " +
        $"{MaxImportExpandedBytes / (1024 * 1024)} MB uncompressed, {MaxImportDataRows} rows, {MaxImportColumns} columns).",
        code: "excel_import_too_large");

    private static ApiException ImportInvalid(Exception inner) => new(
        "The file is not a valid .xlsx workbook.",
        StatusCodes.Status400BadRequest,
        innerException: inner,
        code: "excel_import_invalid");

    private sealed class ImportExpansionBudget
    {
        public long ExpandedBytes;
        public int Elements;
        public int Cells;
    }

    private sealed class BudgetedReadStream(Stream inner, ImportExpansionBudget budget) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Track(inner.Read(buffer));

        private int Track(int read)
        {
            budget.ExpandedBytes += read;
            if (budget.ExpandedBytes > MaxImportExpandedBytes)
                throw ImportTooLarge();
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
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
