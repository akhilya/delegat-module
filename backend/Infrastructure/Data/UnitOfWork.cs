using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PrintO.Infrastructure.Interfaces;

namespace PrintO.Infrastructure.Data;

/// <summary>
/// Unit of Work interface for managing transactions and repositories
/// </summary>
public interface IUnitOfWork : IDisposable
{
    Repository<TEntity> Repository<TEntity>() where TEntity : class, IEntity;
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
}

/// <summary>
/// Unit of Work implementation for managing database operations
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly DataContext _context;
    private readonly Dictionary<Type, object> _repositories = new();
    private IDbContextTransaction? _transaction;

    public UnitOfWork(DataContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Get repository for entity type (cached)
    /// </summary>
    public Repository<TEntity> Repository<TEntity>() where TEntity : class, IEntity
    {
        var type = typeof(TEntity);
        if (!_repositories.ContainsKey(type))
        {
            _repositories[type] = new Repository<TEntity>(_context);
        }
        return (Repository<TEntity>)_repositories[type];
    }

    /// <summary>
    /// Save all changes to database
    /// </summary>
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Begin a database transaction
    /// </summary>
    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        _transaction = await _context.Database.BeginTransactionAsync(ct);
    }

    /// <summary>
    /// Commit the current transaction
    /// </summary>
    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    /// <summary>
    /// Rollback the current transaction
    /// </summary>
    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(ct);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        _transaction?.Dispose();
        _context.Dispose();
    }
}
