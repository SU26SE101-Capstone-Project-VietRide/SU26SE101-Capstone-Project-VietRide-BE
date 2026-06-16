using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class TopUpRequestRepository : ITopUpRequestRepository
{
    private readonly PaymentDbContext _db;

    public TopUpRequestRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<TopUpRequest?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.TopUpRequests.FirstOrDefaultAsync(topUp => topUp.Id == id, ct);

    public async Task<TopUpRequest> AddAsync(TopUpRequest entity, CancellationToken ct)
    {
        await _db.TopUpRequests.AddAsync(entity, ct);
        return entity;
    }

    public void Update(TopUpRequest entity)
        => _db.TopUpRequests.Update(entity);

    public void Remove(TopUpRequest entity)
        => _db.TopUpRequests.Remove(entity);

    public IQueryable<TopUpRequest> Query()
        => _db.TopUpRequests;

    public IQueryable<TopUpRequest> QueryNoTracking()
        => _db.TopUpRequests.AsNoTracking();

    public async Task<TopUpRequest?> FindByVnPayTxnRefAsync(string vnPayTxnRef, CancellationToken cancellationToken)
        => await _db.TopUpRequests.FirstOrDefaultAsync(
            topUp => topUp.VnPayTxnRef == vnPayTxnRef,
            cancellationToken);

    public async Task<TopUpRequest?> FindPendingByVnPayTxnRefForUpdateAsync(
        string vnPayTxnRef,
        CancellationToken cancellationToken)
        => await _db.TopUpRequests
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_payment.top_up_requests
                WHERE vnpay_txn_ref = {vnPayTxnRef}
                  AND status = 'PENDING'
                FOR UPDATE
                """)
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<int> ExpirePendingOlderThanAsync(
        DateTimeOffset expiresBefore,
        DateTimeOffset expiredAt,
        CancellationToken cancellationToken)
        => await _db.TopUpRequests
            .Where(topUp => topUp.Status == TopUpRequestStatus.PENDING && topUp.CreatedAt < expiresBefore)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(topUp => topUp.Status, TopUpRequestStatus.EXPIRED)
                    .SetProperty(topUp => topUp.ExpiredAt, expiredAt)
                    .SetProperty(topUp => topUp.UpdatedAt, expiredAt),
                cancellationToken);
}
