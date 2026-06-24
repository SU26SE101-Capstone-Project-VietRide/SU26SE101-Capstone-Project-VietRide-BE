using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class RefundFailureLogRepository : IRefundFailureLogRepository
{
    private readonly PaymentDbContext _db;

    public RefundFailureLogRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<RefundFailureLog?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.RefundFailureLogs.FirstOrDefaultAsync(log => log.Id == id, ct);

    public async Task<RefundFailureLog> AddAsync(RefundFailureLog entity, CancellationToken ct)
    {
        await _db.RefundFailureLogs.AddAsync(entity, ct);
        return entity;
    }

    public void Update(RefundFailureLog entity)
        => _db.RefundFailureLogs.Update(entity);

    public void Remove(RefundFailureLog entity)
        => _db.RefundFailureLogs.Remove(entity);

    public IQueryable<RefundFailureLog> Query()
        => _db.RefundFailureLogs;

    public IQueryable<RefundFailureLog> QueryNoTracking()
        => _db.RefundFailureLogs.AsNoTracking();

    public async Task<IReadOnlyList<RefundFailureLog>> GetRetryableAsync(
        int maxRetryCount,
        CancellationToken cancellationToken)
        => await _db.RefundFailureLogs
            .Where(log => log.ResolvedAt == null && log.RetryCount < maxRetryCount)
            .OrderBy(log => log.LastAttemptAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RefundFailureLog>> GetUnresolvedAsync(CancellationToken cancellationToken)
        => await _db.RefundFailureLogs
            .Where(log => log.ResolvedAt == null)
            .OrderBy(log => log.LastAttemptAt)
            .ToListAsync(cancellationToken);
}
