using Delegat.Models;
using Delegat.Services;

namespace Delegat.Tests;

public class ProductImportCategoryWaitTests
{
    [Fact]
    public void Ready_WaitsWhileCategoryJobInFlight()
    {
        var pending = new PendingProductImport
        {
            connectionId = 1,
            externalCategoryId = "236",
        };

        Assert.False(ProductImportBackgroundService.IsProductImportReady(
            pending, new(), new()));
    }

    [Fact]
    public void Ready_WhenMappingExists()
    {
        var pending = new PendingProductImport
        {
            connectionId = 1,
            externalCategoryId = "236",
        };
        var mapping = new Dictionary<(int connectionId, string externalCategoryId), int>
        {
            [(1, "236")] = 9,
        };

        Assert.True(ProductImportBackgroundService.IsProductImportReady(
            pending, mapping, new()));
    }

    [Fact]
    public void Ready_WhenForceWithoutMapping()
    {
        var pending = new PendingProductImport
        {
            connectionId = 1,
            externalCategoryId = "236",
            forceImportWithoutMapping = true,
        };

        Assert.True(ProductImportBackgroundService.IsProductImportReady(
            pending, new(), new()));
    }

    [Fact]
    public void Ready_WhenCategoryJobExhausted()
    {
        var pending = new PendingProductImport
        {
            connectionId = 1,
            externalCategoryId = "236",
        };
        var failed = new Dictionary<(int connectionId, string externalCategoryId), string>
        {
            [(1, "236")] = "Category import failed",
        };

        Assert.True(ProductImportBackgroundService.IsProductImportReady(
            pending, new(), failed));
    }

    [Fact]
    public void Ready_WhenTargetCategoryAlreadySet()
    {
        var pending = new PendingProductImport
        {
            connectionId = 1,
            externalCategoryId = "236",
            targetCategoryId = 4,
        };

        Assert.True(ProductImportBackgroundService.IsProductImportReady(
            pending, new(), new()));
    }
}
