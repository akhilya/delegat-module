using Microsoft.EntityFrameworkCore;
using PrintO.Infrastructure.Interfaces;
using System.Linq.Expressions;

namespace PrintO.Infrastructure.Data;

/// <summary>
/// Generic repository for CRUD operations on entities
/// Replaces Zorro ModelRepository
/// </summary>
/// <typeparam name="TEntity">Entity type that implements IEntity</typeparam>
public class Repository<TEntity> where TEntity : class, IEntity
{
    private readonly DbContext _context;
    private readonly DbSet<TEntity> _dbSet;

    public Repository(DbContext context)
    {
        _context = context;
        _dbSet = context.Set<TEntity>();
    }

    /// <summary>
    /// Get entity by ID (no tracking)
    /// </summary>
    public async Task<TEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    /// <summary>
    /// Get entity by ID with includes (no tracking)
    /// </summary>
    public async Task<TEntity?> GetByIdWithIncludesAsync(
        int id,
        CancellationToken ct = default,
        params Expression<Func<TEntity, object>>[] includes)
    {
        IQueryable<TEntity> query = _dbSet.AsNoTracking();
        foreach (var include in includes)
        {
            query = query.Include(include);
        }
        return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    /// <summary>
    /// Get entity by ID with query builder for complex includes (no tracking)
    /// </summary>
    public async Task<TEntity?> GetByIdWithIncludesAsync(
        int id,
        Func<IQueryable<TEntity>, IQueryable<TEntity>> queryBuilder,
        CancellationToken ct = default)
    {
        IQueryable<TEntity> query = _dbSet.AsNoTracking();
        query = queryBuilder(query);
        return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    /// <summary>
    /// Get entity by ID with tracking (for updates)
    /// </summary>
    public async Task<TEntity?> GetByIdTrackingAsync(int id, CancellationToken ct = default)
    {
        return await _dbSet.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    /// <summary>
    /// Get entity by ID with includes and tracking (for updates with navigation properties)
    /// </summary>
    public async Task<TEntity?> GetByIdWithIncludesTrackingAsync(
        int id,
        Func<IQueryable<TEntity>, IQueryable<TEntity>> queryBuilder,
        CancellationToken ct = default)
    {
        IQueryable<TEntity> query = _dbSet;
        query = queryBuilder(query);
        return await query.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    /// <summary>
    /// Get all entities
    /// </summary>
    public async Task<List<TEntity>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().ToListAsync(ct);
    }

    /// <summary>
    /// Get all entities matching a predicate
    /// </summary>
    public async Task<List<TEntity>> GetAllAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().Where(predicate).ToListAsync(ct);
    }

    /// <summary>
    /// Find all entities matching a predicate (alias for GetAllAsync)
    /// </summary>
    public async Task<List<TEntity>> FindAllAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().Where(predicate).ToListAsync(ct);
    }

    /// <summary>
    /// Find all entities with query builder for Include support
    /// </summary>
    public async Task<List<TEntity>> FindAllAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IQueryable<TEntity>> queryBuilder,
        CancellationToken ct = default)
    {
        IQueryable<TEntity> query = _dbSet.AsNoTracking().Where(predicate);
        query = queryBuilder(query);
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Get all entities with includes
    /// </summary>
    public async Task<List<TEntity>> GetAllWithIncludesAsync(
        CancellationToken ct = default,
        params Expression<Func<TEntity, object>>[] includes)
    {
        IQueryable<TEntity> query = _dbSet.AsNoTracking();
        foreach (var include in includes)
        {
            query = query.Include(include);
        }
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Get all entities matching predicate with includes
    /// </summary>
    public async Task<List<TEntity>> GetAllWithIncludesAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default,
        params Expression<Func<TEntity, object>>[] includes)
    {
        IQueryable<TEntity> query = _dbSet.AsNoTracking().Where(predicate);
        foreach (var include in includes)
        {
            query = query.Include(include);
        }
        return await query.ToListAsync(ct);
    }

    /// <summary>
    /// Find single entity matching a predicate
    /// </summary>
    public async Task<TEntity?> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(predicate, ct);
    }

    /// <summary>
    /// Find single entity matching predicate with query builder (for Include support)
    /// </summary>
    public async Task<TEntity?> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IQueryable<TEntity>> queryBuilder,
        CancellationToken ct = default)
    {
        IQueryable<TEntity> query = _dbSet.AsNoTracking();
        query = queryBuilder(query);
        return await query.FirstOrDefaultAsync(predicate, ct);
    }

    /// <summary>
    /// Find single entity matching predicate with includes
    /// </summary>
    public async Task<TEntity?> FindWithIncludesAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default,
        params Expression<Func<TEntity, object>>[] includes)
    {
        IQueryable<TEntity> query = _dbSet.AsNoTracking();
        foreach (var include in includes)
        {
            query = query.Include(include);
        }
        return await query.FirstOrDefaultAsync(predicate, ct);
    }

    /// <summary>
    /// Add a new entity
    /// </summary>
    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken ct = default)
    {
        var entry = await _dbSet.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
        return entry.Entity;
    }

    /// <summary>
    /// Add entity with specific ID (for cases where ID is pre-generated)
    /// </summary>
    public async Task<TEntity> AddWithIdAsync(TEntity entity, int id, CancellationToken ct = default)
    {
        entity.Id = id;
        var entry = await _dbSet.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
        return entry.Entity;
    }

    /// <summary>
    /// Update an existing entity
    /// </summary>
    public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>
    /// Delete an entity
    /// </summary>
    public async Task DeleteAsync(TEntity entity, CancellationToken ct = default)
    {
        _dbSet.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Delete entity by ID
    /// </summary>
    public async Task<bool> DeleteByIdAsync(int id, CancellationToken ct = default)
    {
        var entity = await GetByIdTrackingAsync(id, ct);
        if (entity == null)
            return false;

        _dbSet.Remove(entity);
        await _context.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Check if entity with given ID exists
    /// </summary>
    public async Task<bool> ExistsAsync(int id, CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(e => e.Id == id, ct);
    }

    /// <summary>
    /// Check if any entity matches predicate
    /// </summary>
    public async Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(predicate, ct);
    }

    /// <summary>
    /// Count entities matching predicate
    /// </summary>
    public async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        if (predicate == null)
            return await _dbSet.CountAsync(ct);
        return await _dbSet.CountAsync(predicate, ct);
    }

    /// <summary>
    /// Get queryable for complex queries
    /// </summary>
    public IQueryable<TEntity> Query()
    {
        return _dbSet.AsNoTracking();
    }

    /// <summary>
    /// Generate next available ID
    /// </summary>
    public async Task<int> GenerateNextIdAsync(CancellationToken ct = default)
    {
        var maxId = await _dbSet.MaxAsync(e => (int?)e.Id, ct) ?? 0;
        return maxId + 1;
    }
}
