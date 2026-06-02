namespace VietRide.Shared.Application.UnitOfWork;

/// Optional explicit transaction boundary. For simple flows, MediatR TransactionBehavior handles
/// the retry-safe transaction boundary around the Handler. Use IUnitOfWork directly for multi-step orchestration.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct);
    Task BeginTransactionAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);
}
