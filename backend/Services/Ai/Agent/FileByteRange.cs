using Microsoft.Net.Http.Headers;

namespace Delegat.Services.Ai.Agent;

public readonly record struct FileByteRange(long Start, long End)
{
    public long Length => End - Start + 1;
}

public static class FileByteRangeParser
{
    public static bool TryParse(string? header, long fileLength, out FileByteRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(header))
            return true;
        if (fileLength < 0
            || !RangeHeaderValue.TryParse(header, out var parsed)
            || parsed == null
            || !parsed.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase)
            || parsed.Ranges.Count != 1)
            return false;

        var item = parsed.Ranges.Single();
        if (fileLength == 0)
            return false;

        long start;
        long end;
        if (item.From is long from)
        {
            if (from < 0 || from >= fileLength)
                return false;
            start = from;
            end = item.To is long to ? Math.Min(to, fileLength - 1) : fileLength - 1;
            if (end < start)
                return false;
        }
        else if (item.To is long suffixLength && suffixLength > 0)
        {
            start = Math.Max(0, fileLength - suffixLength);
            end = fileLength - 1;
        }
        else
        {
            return false;
        }

        range = new FileByteRange(start, end);
        return true;
    }
}
