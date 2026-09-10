using Delegat.Models;
using Delegat.Models.Products;
using Delegat.Services.Marketplace;

namespace Delegat.Tests;

public class MedusaLocalGroupExportTests
{
    [Fact]
    public void AddLocalGroup_EmptyMappedGroupId_StillAttachesPimGroup()
    {
        var product = ProductWithGroup(16, "Genshin Impact");
        var export = new Dictionary<string, object?> { ["groupId"] = null };

        MappingService.AddLocalGroup(product, export);

        Assert.Equal("16", export["_localGroupId"]);
        Assert.Equal("Genshin Impact", export["_localGroupName"]);
    }

    [Fact]
    public void AttachLocalProductGroup_EmptyMappedGroupId_WritesPimGroup()
    {
        var product = ProductWithGroup(16, "Genshin Impact");
        var export = new Dictionary<string, object?> { ["groupId"] = null };

        ProductSyncService.AttachLocalProductGroup(export, product, "medusa");

        Assert.Equal("16", export["group_id"]);
        Assert.Equal("Genshin Impact", export["group_name"]);
    }

    [Fact]
    public void AttachLocalProductGroup_OverwritesMappedGroupIdWithPimGroup()
    {
        var product = ProductWithGroup(8, "Warhammer 40K");
        var export = new Dictionary<string, object?> { ["groupId"] = "971702637" };

        ProductSyncService.AttachLocalProductGroup(export, product, "medusa");

        Assert.Equal("8", export["group_id"]);
        Assert.Equal("Warhammer 40K", export["group_name"]);
    }

    [Fact]
    public void AttachLocalProductGroup_IgnoresNonMedusaAdapters()
    {
        var product = ProductWithGroup(8, "Warhammer 40K");
        var export = new Dictionary<string, object?>();

        ProductSyncService.AttachLocalProductGroup(export, product, "ozon");

        Assert.False(export.ContainsKey("group_id"));
    }

    private static Product ProductWithGroup(int groupId, string groupName)
    {
        var group = new ProductGroup { Id = groupId, name = groupName };
        return new Product
        {
            groupMemberships =
            [
                new ProductGroupMember { groupId = groupId, group = group, sortOrder = 0 }
            ]
        };
    }
}
