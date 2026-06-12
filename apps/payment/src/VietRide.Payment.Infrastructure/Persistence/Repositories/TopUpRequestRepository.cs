using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

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
}
