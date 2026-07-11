using Microsoft.EntityFrameworkCore;
using Delegat.Infrastructure.Interfaces;
using System.Linq.Expressions;

namespace Delegat.Infrastructure.Data;

/// <summary>
/// Generic repository providing CRUD and query operations for entities.
/// Supports both immediate-save and batch (no-save) operations for use with <see cref="UnitOfWork"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type, which must implement <see cref="IEntity"/>.</typeparam>
public class Repository<TEntity> where TEntity : class, IEntity
{
    private readonly DbContext _context;
    private readonly DbSet<TEntity> _dbSet;

    /// <summary>
    /// Initializes a new instance of the <see cref="Repository{TEntity}"/> class.
    /// </summary>
    /// <param name="context">The Entity Framework database context.</param>
    public Repository(DbContext context)
    {
        _context = context;
        _dbSet = context.Set<TEntity>();
    }

    /// <summary>
    /// Gets an entity by its primary key without change tracking.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entity if found; otherwise <c>null</c>.</returns>
    public async Task<TEntity?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    /// <summary>
    /// Gets an entity by its primary key with eager-loaded navigation properties, without change tracking.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="includes">Navigation property expressions to include.</param>
    /// <returns>The entity if found; otherwise <c>null</c>.</returns>
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
    /// Gets an entity by its primary key using a query builder for complex includes, without change tracking.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="queryBuilder">A function to apply custom query operations such as nested includes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The entity if found; otherwise <c>null</c>.</returns>
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
    /// Gets an entity by its primary key with change tracking enabled, suitable for update operations.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tracked entity if found; otherwise <c>null</c>.</returns>
    public async Task<TEntity?> GetByIdTrackingAsync(int id, CancellationToken ct = default)
    {
        return await _dbSet.FirstOrDefaultAsync(e => e.Id == id, ct);
    }

    /// <summary>
    /// Gets an entity by its primary key with includes and change tracking enabled,
    /// suitable for update operations that involve navigation properties.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="queryBuilder">A function to apply custom query operations such as includes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tracked entity if found; otherwise <c>null</c>.</returns>
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
    /// Gets all entities without change tracking.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of all entities.</returns>
    public async Task<List<TEntity>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().ToListAsync(ct);
    }

    /// <summary>
    /// Gets all entities matching the specified predicate without change tracking.
    /// </summary>
    /// <param name="predicate">A filter expression to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching entities.</returns>
    public async Task<List<TEntity>> GetAllAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().Where(predicate).ToListAsync(ct);
    }

    /// <summary>
    /// Finds all entities matching the specified predicate without change tracking.
    /// This is an alias for <see cref="GetAllAsync(Expression{Func{TEntity, bool}}, CancellationToken)"/>.
    /// </summary>
    /// <param name="predicate">A filter expression to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching entities.</returns>
    public async Task<List<TEntity>> FindAllAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().Where(predicate).ToListAsync(ct);
    }

    /// <summary>
    /// Finds all entities matching the specified predicate, with a query builder for include support.
    /// </summary>
    /// <param name="predicate">A filter expression to apply.</param>
    /// <param name="queryBuilder">A function to apply custom query operations such as includes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching entities.</returns>
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
    /// Gets all entities with eager-loaded navigation properties, without change tracking.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="includes">Navigation property expressions to include.</param>
    /// <returns>A list of all entities with the specified includes loaded.</returns>
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
    /// Gets all entities matching a predicate with eager-loaded navigation properties, without change tracking.
    /// </summary>
    /// <param name="predicate">A filter expression to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="includes">Navigation property expressions to include.</param>
    /// <returns>A list of matching entities with the specified includes loaded.</returns>
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
    /// Finds the first entity matching the specified predicate without change tracking.
    /// </summary>
    /// <param name="predicate">A filter expression to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first matching entity, or <c>null</c> if none found.</returns>
    public async Task<TEntity?> FindAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(predicate, ct);
    }

    /// <summary>
    /// Finds the first entity matching the specified predicate using a query builder, without change tracking.
    /// </summary>
    /// <param name="predicate">A filter expression to apply.</param>
    /// <param name="queryBuilder">A function to apply custom query operations such as includes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first matching entity, or <c>null</c> if none found.</returns>
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
    /// Finds the first entity matching the specified predicate with eager-loaded navigation properties.
    /// </summary>
    /// <param name="predicate">A filter expression to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="includes">Navigation property expressions to include.</param>
    /// <returns>The first matching entity, or <c>null</c> if none found.</returns>
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
    /// Adds a new entity to the database and saves changes immediately.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The added entity with its generated identifier.</returns>
    public async Task<TEntity> AddAsync(TEntity entity, CancellationToken ct = default)
    {
        var entry = await _dbSet.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
        return entry.Entity;
    }

    /// <summary>
    /// Adds a new entity with a pre-assigned identifier and saves changes immediately.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="id">The pre-generated identifier to assign.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The added entity.</returns>
    public async Task<TEntity> AddWithIdAsync(TEntity entity, int id, CancellationToken ct = default)
    {
        entity.Id = id;
        var entry = await _dbSet.AddAsync(entity, ct);
        await _context.SaveChangesAsync(ct);
        return entry.Entity;
    }

    /// <summary>
    /// Marks an existing entity as modified and saves changes immediately.
    /// </summary>
    /// <param name="entity">The entity to update.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated entity.</returns>
    public async Task<TEntity> UpdateAsync(TEntity entity, CancellationToken ct = default)
    {
        _context.Entry(entity).State = EntityState.Modified;
        await _context.SaveChangesAsync(ct);
        return entity;
    }

    /// <summary>
    /// Removes an entity from the database and saves changes immediately.
    /// </summary>
    /// <param name="entity">The entity to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DeleteAsync(TEntity entity, CancellationToken ct = default)
    {
        _dbSet.Remove(entity);
        await _context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Finds and removes an entity by its primary key, saving changes immediately.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the entity was found and deleted; <c>false</c> if not found.</returns>
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
    /// Checks whether an entity with the specified identifier exists.
    /// </summary>
    /// <param name="id">The entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the entity exists; otherwise <c>false</c>.</returns>
    public async Task<bool> ExistsAsync(int id, CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(e => e.Id == id, ct);
    }

    /// <summary>
    /// Checks whether any entity matches the specified predicate.
    /// </summary>
    /// <param name="predicate">A filter expression to test.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if any entity matches; otherwise <c>false</c>.</returns>
    public async Task<bool> AnyAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbSet.AnyAsync(predicate, ct);
    }

    /// <summary>
    /// Counts entities, optionally filtered by a predicate.
    /// </summary>
    /// <param name="predicate">An optional filter expression. If <c>null</c>, all entities are counted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of matching entities.</returns>
    public async Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken ct = default)
    {
        if (predicate == null)
            return await _dbSet.CountAsync(ct);
        return await _dbSet.CountAsync(predicate, ct);
    }

    /// <summary>
    /// Returns a no-tracking queryable for building custom queries.
    /// </summary>
    /// <returns>An <see cref="IQueryable{TEntity}"/> with no change tracking.</returns>
    public IQueryable<TEntity> Query()
    {
        return _dbSet.AsNoTracking();
    }

    /// <summary>
    /// Returns a tracking queryable for update operations that need relationship changes.
    /// </summary>
    /// <returns>An <see cref="IQueryable{TEntity}"/> with change tracking enabled.</returns>
    public IQueryable<TEntity> QueryTracking()
    {
        return _dbSet;
    }

    /// <summary>
    /// Generates the next available identifier by finding the current maximum and adding one.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The next available integer identifier.</returns>
    public async Task<int> GenerateNextIdAsync(CancellationToken ct = default)
    {
        var maxId = await _dbSet.MaxAsync(e => (int?)e.Id, ct) ?? 0;
        return maxId + 1;
    }

    // ---- Batch methods (no SaveChanges - caller must save via UnitOfWork) ----

    /// <summary>
    /// Adds multiple entities without saving. The caller must call <c>SaveChangesAsync</c> via <see cref="UnitOfWork"/>.
    /// </summary>
    /// <param name="entities">The entities to add.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken ct = default)
    {
        await _dbSet.AddRangeAsync(entities, ct);
    }

    /// <summary>
    /// Adds a single entity without saving. The caller must call <c>SaveChangesAsync</c> via <see cref="UnitOfWork"/>.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task AddWithoutSaveAsync(TEntity entity, CancellationToken ct = default)
    {
        await _dbSet.AddAsync(entity, ct);
    }

    /// <summary>
    /// Removes multiple entities without saving. The caller must call <c>SaveChangesAsync</c> via <see cref="UnitOfWork"/>.
    /// </summary>
    /// <param name="entities">The entities to remove.</param>
    public void DeleteRange(IEnumerable<TEntity> entities)
    {
        _dbSet.RemoveRange(entities);
    }

    /// <summary>
    /// Removes a single entity without saving. The caller must call <c>SaveChangesAsync</c> via <see cref="UnitOfWork"/>.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    public void DeleteWithoutSave(TEntity entity)
    {
        _dbSet.Remove(entity);
    }

    /// <summary>
    /// Marks an entity as modified without saving. The caller must call <c>SaveChangesAsync</c> via <see cref="UnitOfWork"/>.
    /// </summary>
    /// <param name="entity">The entity to mark as modified.</param>
    public void UpdateWithoutSave(TEntity entity)
    {
        _context.Entry(entity).State = EntityState.Modified;
    }

    /// <summary>
    /// Executes a server-side paged query with COUNT and LIMIT/OFFSET in the database.
    /// </summary>
    /// <param name="predicate">A filter expression to apply.</param>
    /// <param name="queryBuilder">An optional function to apply custom query operations such as includes.</param>
    /// <param name="orderBy">A function to apply ordering to the query.</param>
    /// <param name="skip">The number of entities to skip (offset).</param>
    /// <param name="take">The number of entities to return (page size).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple containing the page of entities and the total count of matching records.</returns>
    public async Task<(List<TEntity> items, int totalCount)> FindPagedAsync(
        Expression<Func<TEntity, bool>> predicate,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? queryBuilder,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> orderBy,
        int skip,
        int take,
        CancellationToken ct = default)
    {
        IQueryable<TEntity> query = _dbSet.AsNoTracking().Where(predicate);

        var totalCount = await query.CountAsync(ct);

        if (queryBuilder != null)
            query = queryBuilder(query);

        query = orderBy(query);
        var items = await query.Skip(skip).Take(take).ToListAsync(ct);

        return (items, totalCount);
    }
}
