using Delegat.Services.FolderJobs;

namespace Delegat.Tests;

public class FolderTreeStatsTests
{
    [Fact]
    public void ComputeTotals_Empty_ReturnsEmpty()
    {
        var result = FolderTreeStats.ComputeTotals(Array.Empty<FolderTreeStats.FolderStatRow>());
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeTotals_LeafOnly_UsesDirectCounts()
    {
        var rows = new[]
        {
            new FolderTreeStats.FolderStatRow(1, null, FileCount: 3, SubfolderCount: 0, DirectSizeBytes: 150),
        };

        var result = FolderTreeStats.ComputeTotals(rows);

        Assert.Equal(new FolderTreeStats.FolderTotals(3, 150), result[1]);
    }

    [Fact]
    public void ComputeTotals_NestedTree_AggregatesDescendantsIntoParents()
    {
        // root(1)
        //  ├─ child(2)  → 2 files / 20 bytes
        //  │   └─ leaf(3) → 1 file / 5 bytes
        //  └─ sibling(4) → 4 files / 40 bytes
        var rows = new[]
        {
            new FolderTreeStats.FolderStatRow(1, null, 0, 2, 0),
            new FolderTreeStats.FolderStatRow(2, 1, 2, 1, 20),
            new FolderTreeStats.FolderStatRow(3, 2, 1, 0, 5),
            new FolderTreeStats.FolderStatRow(4, 1, 4, 0, 40),
        };

        var result = FolderTreeStats.ComputeTotals(rows);

        Assert.Equal(new FolderTreeStats.FolderTotals(1, 5), result[3]);
        Assert.Equal(new FolderTreeStats.FolderTotals(3, 25), result[2]); // 2+1, 20+5
        Assert.Equal(new FolderTreeStats.FolderTotals(4, 40), result[4]);
        Assert.Equal(new FolderTreeStats.FolderTotals(7, 65), result[1]); // 0+3+4, 0+25+40
    }

    [Fact]
    public void ComputeTotals_OrphanParentReference_IgnoresMissingParentLink()
    {
        // Child points at a parent id that is not in the row set; still totals itself.
        var rows = new[]
        {
            new FolderTreeStats.FolderStatRow(10, ParentFolderId: 999, FileCount: 2, SubfolderCount: 0, DirectSizeBytes: 8),
        };

        var result = FolderTreeStats.ComputeTotals(rows);

        Assert.Equal(new FolderTreeStats.FolderTotals(2, 8), result[10]);
    }
}
