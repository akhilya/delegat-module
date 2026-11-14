using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using System.Reflection;
using Delegat.Models.Marketplace;
using Delegat.Models.Marketplace.DTOs;
using Delegat.Services;
using Delegat.Services.Marketplace;
using Delegat.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Delegat.Tests;

internal sealed class CatalogWriteFault : DbCommandInterceptor
{
    public Func<DbCommand, bool>? ShouldFail { get; set; }
    public Action? BeforeFailure { get; set; }
    public int Failures { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        if (ShouldFail?.Invoke(command) == true)
        {
            Failures++;
            BeforeFailure?.Invoke();
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Injected catalog write failure");
        }
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }
}

internal class RecordingCatalogLogger(DataContext db) : IOperationLogger
{
    public List<(int Total, int Completed, int Failed)> Progress { get; } = [];
    public Func<int, int, Task>? OnProgress { get; set; }
    public Task StartAsync(string operationId, int storeId, int authorUserId, string type, string title,
        int? connectionId = null, string? connectionName = null, string? batchId = null,
        string? batchType = null, int? totalItems = null, int? authorApiKeyId = null) => Task.CompletedTask;
    public Task LogAsync(string operationId, int storeId, string level, string message)
    {
        Assert.Null(db.Database.CurrentTransaction);
        return Task.CompletedTask;
    }
    public async Task ProgressAsync(string operationId, int storeId, int total, int completed, int failed)
    {
        Assert.Null(db.Database.CurrentTransaction);
        Progress.Add((total, completed, failed));
        if (OnProgress != null) await OnProgress(completed, failed);
    }
    public Task FinishAsync(string operationId, int storeId, string status, string? summary = null) => Task.CompletedTask;
    public Task UpdateStatusAsync(string operationId, int storeId, string status) => Task.CompletedTask;
}

public class TransactionImportAdapterProxy : DispatchProxy
{
    public ExternalProduct Source { get; set; } = new("external", "SKU", "Imported");
    public DataContext Db { get; set; } = null!;
    public int Calls { get; private set; }
    public bool Cancel { get; set; }
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        switch (method!.Name)
        {
            case "get_Key": return "fake";
            case "get_Capabilities": return MarketplaceCapabilities.ImportProduct;
            case "ImportProductAsync":
                Assert.Null(Db.Database.CurrentTransaction);
                Calls++;
                if (Cancel) throw new OperationCanceledException((CancellationToken)args![2]!);
                return Task.FromResult<ExternalProduct?>(Source);
            default: throw new NotSupportedException(method.Name);
        }
    }
}

internal sealed class TransactionAdapterRegistry(IMarketplaceAdapter adapter) : IMarketplaceAdapterRegistry
{
    public IReadOnlyList<IMarketplaceAdapter> GetAll() => [adapter];
    public IMarketplaceAdapter? GetByKey(string key) => key == "fake" ? adapter : null;
    public bool Exists(string key) => key == "fake";
    public IReadOnlyList<MarketplaceInfo> GetAvailableMarketplaces() => [];
}

internal static class CatalogTransactionTestSupport
{
    public static ProductSyncService ImportService(DataContext db, IMarketplaceAdapter adapter) => new(
        db, new TransactionAdapterRegistry(adapter), new MappingService(db, new ServiceCollection().BuildServiceProvider()), null!, NullLogger<ProductSyncService>.Instance,
        new RecordingCatalogLogger(db), new QuotaService(TestDbFactory.CreateUnitOfWork(db), db));
}
