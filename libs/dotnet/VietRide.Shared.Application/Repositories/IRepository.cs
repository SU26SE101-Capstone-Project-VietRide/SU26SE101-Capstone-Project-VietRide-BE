namespace VietRide.Shared.Application.Repositories;

/// Generic repository contract — concrete impl `EfRepository<TEntity,TId>` lives in VietRide.Shared.Persistence.
/// Per-aggregate repos (e.g. IBookingRepository) extend this and add domain-specific queries.
/// Per BACKEND_SOURCE_OF_TRUTH 3.2.4.
public interface IRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct);
    Task<TEntity> AddAsync(TEntity entity, CancellationToken ct);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    IQueryable<TEntity> Query();
    IQueryable<TEntity> QueryNoTracking();
}

/// Read-only variant — inject into Query Handlers when mutation is not allowed.
public interface IReadRepository<TEntity, TId>
    where TEntity : class
    where TId : notnull
{
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken ct);
    IQueryable<TEntity> QueryNoTracking();
}
