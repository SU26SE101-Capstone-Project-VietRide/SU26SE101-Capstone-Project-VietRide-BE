using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using VietRide.Shared.Application.UnitOfWork;

namespace VietRide.Shared.Persistence.UnitOfWork;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/> backed by <see cref="VietRideDbContextBase"/>.
/// Resolved per-scope alongside the service's DbContext.
/// </summary>
public sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly VietRideDbContextBase _db;
    private IDbContextTransaction? _transaction;

    public EfUnitOfWork(VietRideDbContextBase db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken ct)
        => _db.SaveChangesAsync(ct);

    /// <inheritdoc />
    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (_transaction is not null || _db.Database.CurrentTransaction is not null)
        {
            var joinedResult = await operation().ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return joinedResult;
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                var result = await operation().ConfigureAwait(false);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task BeginTransactionAsync(CancellationToken ct)
    {
        _transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken ct)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException(
                "CommitAsync called without an active transaction. Call BeginTransactionAsync first.");
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _transaction.CommitAsync(ct).ConfigureAwait(false);
        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken ct)
    {
        if (_transaction is null)
        {
            return; // No active transaction — safe no-op.
        }

        await _transaction.RollbackAsync(ct).ConfigureAwait(false);
        await _transaction.DisposeAsync().ConfigureAwait(false);
        _transaction = null;
    }
}
