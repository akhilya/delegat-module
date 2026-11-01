using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Delegat.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Delegat.Services.Ai.Agent;

/// <summary>
/// Extracts bounded, useful content from a live chat attachment without sending its
/// binary bytes through a model/tool payload.
/// </summary>
public sealed class ChatAttachmentReadService
{
    public const long MaxInputBytes = 32 * 1024 * 1024;
    public const int MaxRowsPerRead = 200;
    public const int MaxOutputCharacters = 16_000;
    private const long MaxExpandedWorkbookBytes = 128 * 1024 * 1024;
    private const int MaxWorkbookEntries = 10_000;
    private const int MaxColumnsPerRead = 20;
    private const int MaxCellCharacters = 128;
    private static readonly string[] SupportedFormats = [".xlsx", ".xlsm", ".csv", ".tsv", ".txt", ".md", ".json", ".xml"];

    private readonly DataContext _db;
    private readonly IStorageService _storage;

    public ChatAttachmentReadService(DataContext db, IStorageService storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<(object result, string summary)> ReadAsync(
        int storeId,
        int userId,
        string sessionId,
        int attachmentId,
        string? sheetName,
        int startRow,
        int rowLimit,
        int startColumn,
        int startOffset,
        int maxCharacters,
        CancellationToken ct)
    {
        if (attachmentId <= 0)
            return Failure("attachment_id_required", "A valid chat attachment id is required");

        var attachment = await _db.aiChatAttachments.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == attachmentId
                && item.storeId == storeId
                && item.userId == userId
                && item.sessionId == sessionId, ct);
        if (attachment == null || !AgentFileUploadService.IsLive(attachment.createdAt))
            return Failure("attachment_not_found_or_expired", "The chat attachment is unavailable or expired");

        var extension = Path.GetExtension(attachment.fileName).ToLowerInvariant();
        if (!SupportedFormats.Contains(extension, StringComparer.Ordinal))
        {
            return (new
            {
                error = "unsupported_attachment_format",
                fileName = attachment.fileName,
                supportedFormats = SupportedFormats,
            }, $"Unsupported chat attachment format: {extension}");
        }

        if (attachment.length == 0)
            return Failure("attachment_empty", "The chat attachment is empty");
        if (attachment.length > MaxInputBytes)
        {
            return (new
            {
                error = "attachment_too_large",
                fileName = attachment.fileName,
                sizeBytes = attachment.length,
                maxBytes = MaxInputBytes,
            }, $"Chat attachment exceeds the {MaxInputBytes}-byte inspection limit");
        }

        if (!await _storage.ExistsAsync(attachment.storageKey, ct))
            return Failure("attachment_storage_unavailable", "The chat attachment is unavailable in storage");

        await using var source = await _storage.GetStreamAsync(attachment.storageKey, ct);
        if (source == null)
            return Failure("attachment_storage_unavailable", "The chat attachment is unavailable in storage");

        using var buffer = new MemoryStream((int)Math.Min(attachment.length, MaxInputBytes));
        var copyResult = await CopyBoundedAsync(source, buffer, MaxInputBytes, ct);
        if (copyResult.TooLarge)
            return Failure("attachment_too_large", "The chat attachment exceeds the inspection limit");
        if (copyResult.BytesCopied != attachment.length)
            return Failure("attachment_size_mismatch", "The stored chat attachment does not match its recorded size");

        try
        {
            if (extension is ".xlsx" or ".xlsm")
                return ReadWorkbook(attachment.fileName, buffer, sheetName, startRow, rowLimit, startColumn);

            return ReadText(attachment.fileName, extension, buffer, startOffset, maxCharacters);
        }
        catch (AttachmentArchiveLimitException)
        {
            return Failure("attachment_archive_too_large", "The workbook expands beyond the inspection limit");
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
        {
            return Failure("attachment_parse_failed", "The chat attachment could not be read as a valid file");
        }
    }

    private static (object result, string summary) ReadWorkbook(
        string fileName,
        MemoryStream content,
        string? sheetName,
        int startRow,
        int rowLimit,
        int startColumn)
    {
        EnsureWorkbookArchiveWithinLimits(content);
        content.Position = 0;
        using var workbook = new XLWorkbook(content);
        var sheetInfos = workbook.Worksheets.Select(sheet =>
        {
            var used = sheet.RangeUsed(XLCellsUsedOptions.Contents);
            return new SheetInfo(
                sheet.Name,
                used?.FirstRow().RowNumber() ?? 0,
                used?.LastRow().RowNumber() ?? 0,
                used?.FirstColumn().ColumnNumber() ?? 0,
                used?.LastColumn().ColumnNumber() ?? 0);
        }).ToList();

        if (sheetInfos.Count == 0)
            return (new { fileName, format = "xlsx", sheets = Array.Empty<object>(), rows = Array.Empty<object>() }, $"Read empty workbook {fileName}");

        var selected = string.IsNullOrWhiteSpace(sheetName)
            ? workbook.Worksheet(1)
            : workbook.Worksheets.FirstOrDefault(sheet =>
                string.Equals(sheet.Name, sheetName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (selected == null)
        {
            return (new { error = "worksheet_not_found", fileName, requestedSheet = sheetName, sheets = sheetInfos.Select(s => s.Name) },
                $"Worksheet not found in {fileName}");
        }

        var selectedInfo = sheetInfos.First(sheet => string.Equals(sheet.Name, selected.Name, StringComparison.Ordinal));
        var boundedStart = Math.Max(selectedInfo.FirstRow, Math.Max(1, startRow));
        var boundedLimit = Math.Clamp(rowLimit <= 0 ? 50 : rowLimit, 1, MaxRowsPerRead);
        var rows = new List<object>();
        var outputCharacters = 0;
        var truncated = false;
        var lastReadRow = boundedStart - 1;
        var firstColumn = Math.Max(selectedInfo.FirstColumn, Math.Max(1, startColumn));
        var totalColumns = selectedInfo.LastColumn == 0
            ? 0
            : selectedInfo.LastColumn - selectedInfo.FirstColumn + 1;
        if (totalColumns > 0 && firstColumn > selectedInfo.LastColumn)
        {
            return (new { error = "worksheet_column_out_of_range", fileName, selectedSheet = selected.Name, lastColumn = selectedInfo.LastColumn },
                $"Worksheet column is outside {fileName}");
        }
        var columnsIncluded = totalColumns == 0
            ? 0
            : Math.Min(MaxColumnsPerRead, selectedInfo.LastColumn - firstColumn + 1);
        var nextStartColumn = columnsIncluded > 0 && firstColumn + columnsIncluded <= selectedInfo.LastColumn
            ? firstColumn + columnsIncluded
            : (int?)null;

        for (var rowNumber = boundedStart;
             rowNumber <= selectedInfo.LastRow && rows.Count < boundedLimit;
             rowNumber++)
        {
            var values = new List<string?>();
            for (var column = firstColumn; column < firstColumn + columnsIncluded; column++)
            {
                var value = selected.Cell(rowNumber, column).GetFormattedString(CultureInfo.InvariantCulture);
                if (value.Length > MaxCellCharacters)
                    value = value[..MaxCellCharacters] + "…";
                values.Add(value.Length == 0 ? null : value);
            }

            var row = new { rowNumber, values };
            var rowCharacters = JsonSerializer.Serialize(row).Length;
            if (outputCharacters + rowCharacters > MaxOutputCharacters && rows.Count > 0)
            {
                truncated = true;
                break;
            }

            rows.Add(row);
            outputCharacters += rowCharacters;
            lastReadRow = rowNumber;
            if (outputCharacters >= MaxOutputCharacters)
            {
                truncated = rowNumber < selectedInfo.LastRow;
                break;
            }
        }

        var nextStartRow = lastReadRow < selectedInfo.LastRow ? lastReadRow + 1 : (int?)null;
        return (
            new
            {
                fileName,
                format = "xlsx",
                sheets = sheetInfos.Select(sheet => new
                {
                    name = sheet.Name,
                    firstRow = sheet.FirstRow,
                    lastRow = sheet.LastRow,
                    firstColumn = sheet.FirstColumn,
                    lastColumn = sheet.LastColumn,
                }),
                selectedSheet = selected.Name,
                totalRows = selectedInfo.LastRow == 0 ? 0 : selectedInfo.LastRow - selectedInfo.FirstRow + 1,
                firstRow = selectedInfo.FirstRow,
                firstColumn,
                columnsIncluded,
                totalColumns,
                nextStartColumn,
                columnsTruncated = nextStartColumn.HasValue,
                rows,
                nextStartRow,
                truncated,
            },
            $"Read {rows.Count} rows from {fileName} ({selected.Name})");
    }

    private static (object result, string summary) ReadText(
        string fileName,
        string extension,
        MemoryStream content,
        int startOffset,
        int maxCharacters)
    {
        content.Position = 0;
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = reader.ReadToEnd();
        var offset = Math.Max(0, startOffset);
        if (offset > text.Length)
        {
            return (new { error = "attachment_offset_out_of_range", fileName, textLength = text.Length },
                $"Text offset is outside {fileName}");
        }

        var pageLimit = Math.Clamp(maxCharacters <= 0 ? 12_000 : maxCharacters, 1, MaxOutputCharacters);
        var pageLength = Math.Min(pageLimit, text.Length - offset);
        var nextOffset = offset + pageLength < text.Length ? offset + pageLength : (int?)null;
        var page = text.Substring(offset, pageLength);
        return (
            new
            {
                fileName,
                format = extension.TrimStart('.'),
                text = page,
                startOffset = offset,
                nextStartOffset = nextOffset,
                totalCharacters = text.Length,
                truncated = nextOffset.HasValue,
            },
            $"Read {fileName}{(nextOffset.HasValue ? " (more text available)" : "")}");
    }

    private static async Task<(long BytesCopied, bool TooLarge)> CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0)
                return (copied, false);
            if (copied + read > maxBytes)
                return (copied, true);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;
        }
    }

    private static (object result, string summary) Failure(string code, string summary) =>
        (new { error = code }, summary);

    private static void EnsureWorkbookArchiveWithinLimits(Stream content)
    {
        content.Position = 0;
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > MaxWorkbookEntries)
            throw new AttachmentArchiveLimitException();

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length < 0 || entry.Length > MaxExpandedWorkbookBytes - expandedBytes)
                throw new AttachmentArchiveLimitException();
            expandedBytes += entry.Length;
        }
    }

    private sealed record SheetInfo(string Name, int FirstRow, int LastRow, int FirstColumn, int LastColumn);
    private sealed class AttachmentArchiveLimitException : Exception { }
}
