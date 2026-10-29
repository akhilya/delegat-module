using Microsoft.EntityFrameworkCore;
using Delegat.Models;
using Delegat.Models.Marketplace;
using Delegat.Services;
using Delegat.Services.Marketplace;

namespace Delegat.Services.ScheduledTasks;

public abstract class ScheduledTaskMarketplaceActionBase : IScheduledTaskAction
{
    private readonly IOperationLogger _operations;
    private readonly MarketplaceActionExecutor _executor;
    protected DataContext Db { get; }

    protected ScheduledTaskMarketplaceActionBase(
        DataContext db,
        IOperationLogger operations,
        MarketplaceActionExecutor executor)
    {
        Db = db;
        _operations = operations;
        _executor = executor;
    }

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }

    public async Task ExecuteAsync(ScheduledTask task, CancellationToken ct)
    {
        var parameters = ReadParameters(task.parametersJson);
        var connectionId = parameters.GetValueOrDefault("connectionId", 0);
        if (connectionId <= 0)
            throw new InvalidOperationException("A valid marketplace connection must be selected.");

        var connection = await Db.marketplaceConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == connectionId && x.storeId == task.storeId, ct)
            ?? throw new InvalidOperationException("The selected connection does not belong to this store.");
        if (!connection.isActive)
            throw new InvalidOperationException("The selected marketplace connection is inactive.");

        var operationId = Guid.NewGuid().ToString("N");
        await _operations.StartAsync(operationId, task.storeId, task.authorUserId,
            OperationType(Id), task.name, connection.Id, connection.name,
            batchId: operationId, batchType: "scheduled-task");
        try
        {
            await _operations.LogAsync(operationId, task.storeId, "info", "Scheduled task started.");
            var summary = await _executor.ExecuteAsync(Id, connection, task.storeId,
                task.authorUserId, parameters, operationId, ct);
            await _operations.LogAsync(operationId, task.storeId, "success", summary);
            await _operations.FinishAsync(operationId, task.storeId, "completed", summary);
        }
        catch (Exception ex)
        {
            await _operations.LogAsync(operationId, task.storeId, "error", ex.Message);
            await _operations.FinishAsync(operationId, task.storeId, "failed", ex.Message);
            throw;
        }
    }

    private static Dictionary<string, int> ReadParameters(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
            if (property.Value.TryGetInt32(out var value)) values[property.Name] = value;
        return values;
    }

    private static string OperationType(string id) => id switch
    {
        "export-prices" => "scheduled-price-export",
        "export-stocks" => "scheduled-stock-export",
        "import-prices" => "scheduled-price-import",
        "import-products" => "scheduled-product-import",
        "verify-product-links" => "scheduled-link-verification",
        "check-export-readiness" => "scheduled-export-readiness",
        "compare-prices" => "scheduled-price-comparison",
        _ => $"scheduled-{id}",
    };
}

public sealed class ExportPricesScheduledTaskAction : ScheduledTaskMarketplaceActionBase
{
    public ExportPricesScheduledTaskAction(DataContext db, IOperationLogger operations,
        MarketplaceActionExecutor executor) : base(db, operations, executor) { }
    public override string Id => "export-prices";
    public override string DisplayName => "Export prices";
    public override string Description => "Exports linked product prices to the selected marketplace connection.";
}

public sealed class ExportStocksScheduledTaskAction : ScheduledTaskMarketplaceActionBase
{
    public ExportStocksScheduledTaskAction(DataContext db, IOperationLogger operations,
        MarketplaceActionExecutor executor) : base(db, operations, executor) { }
    public override string Id => "export-stocks";
    public override string DisplayName => "Export stocks";
    public override string Description => "Exports stock levels for linked products to the selected marketplace connection.";
}

public sealed class ImportPricesScheduledTaskAction : ScheduledTaskMarketplaceActionBase
{
    public ImportPricesScheduledTaskAction(DataContext db, IOperationLogger operations,
        MarketplaceActionExecutor executor) : base(db, operations, executor) { }
    public override string Id => "import-prices";
    public override string DisplayName => "Import prices";
    public override string Description => "Updates local product prices and stock cache from the selected marketplace connection.";
}

public sealed class ImportProductsScheduledTaskAction : ScheduledTaskMarketplaceActionBase
{
    public ImportProductsScheduledTaskAction(DataContext db, IOperationLogger operations,
        MarketplaceActionExecutor executor) : base(db, operations, executor) { }
    public override string Id => "import-products";
    public override string DisplayName => "Import new products";
    public override string Description => "Imports new marketplace products, up to the configured per-run limit.";
}

public sealed class VerifyProductLinksScheduledTaskAction : ScheduledTaskMarketplaceActionBase
{
    public VerifyProductLinksScheduledTaskAction(DataContext db, IOperationLogger operations,
        MarketplaceActionExecutor executor) : base(db, operations, executor) { }
    public override string Id => "verify-product-links";
    public override string DisplayName => "Verify product links";
    public override string Description => "Checks linked marketplace products and updates missing-link statuses.";
}

public sealed class CheckExportReadinessScheduledTaskAction : ScheduledTaskMarketplaceActionBase
{
    public CheckExportReadinessScheduledTaskAction(DataContext db, IOperationLogger operations,
        MarketplaceActionExecutor executor) : base(db, operations, executor) { }
    public override string Id => "check-export-readiness";
    public override string DisplayName => "Check export readiness";
    public override string Description => "Checks local products for export issues without publishing anything.";
}

public sealed class ComparePricesScheduledTaskAction : ScheduledTaskMarketplaceActionBase
{
    public ComparePricesScheduledTaskAction(DataContext db, IOperationLogger operations,
        MarketplaceActionExecutor executor) : base(db, operations, executor) { }
    public override string Id => "compare-prices";
    public override string DisplayName => "Compare prices";
    public override string Description => "Compares local and marketplace prices without overwriting local prices.";
}
