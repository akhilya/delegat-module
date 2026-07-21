namespace Delegat.Services.FolderJobs;

public static class FolderTreeStats
{
    public readonly record struct FolderStatRow(
        int Id,
        int? ParentFolderId,
        int FileCount,
        int SubfolderCount,
        long DirectSizeBytes);

    public readonly record struct FolderTotals(int TotalFileCount, long TotalSizeBytes);

    public static Dictionary<int, FolderTotals> ComputeTotals(IReadOnlyList<FolderStatRow> rows)
    {
        var map = rows.ToDictionary(r => r.Id);
        var children = new Dictionary<int, List<int>>();
        foreach (var row in rows)
        {
            if (row.ParentFolderId is not int parentId)
                continue;
            if (!children.TryGetValue(parentId, out var list))
                children[parentId] = list = new List<int>();
            list.Add(row.Id);
        }

        var cache = new Dictionary<int, FolderTotals>(rows.Count);

        FolderTotals Recurse(int id)
        {
            if (cache.TryGetValue(id, out var cached))
                return cached;

            var row = map[id];
            var files = row.FileCount;
            var size = row.DirectSizeBytes;
            if (children.TryGetValue(id, out var kids))
            {
                foreach (var kid in kids)
                {
                    var child = Recurse(kid);
                    files += child.TotalFileCount;
                    size += child.TotalSizeBytes;
                }
            }

            return cache[id] = new FolderTotals(files, size);
        }

        foreach (var row in rows)
            Recurse(row.Id);

        return cache;
    }
}
