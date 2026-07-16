using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class OperatorTripSettlementRepository : IOperatorTripSettlementRepository
{
    private readonly PaymentDbContext _db;
    public OperatorTripSettlementRepository(PaymentDbContext db) => _db = db;
    public Task<OperatorTripSettlement?> GetByIdAsync(Guid id, CancellationToken ct) => _db.OperatorTripSettlements.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<OperatorTripSettlement> AddAsync(OperatorTripSettlement entity, CancellationToken ct) { await _db.OperatorTripSettlements.AddAsync(entity, ct); return entity; }
    public void Update(OperatorTripSettlement entity) => _db.OperatorTripSettlements.Update(entity);
    public void Remove(OperatorTripSettlement entity) => _db.OperatorTripSettlements.Remove(entity);
    public IQueryable<OperatorTripSettlement> Query() => _db.OperatorTripSettlements;
    public IQueryable<OperatorTripSettlement> QueryNoTracking() => _db.OperatorTripSettlements.AsNoTracking();
    public Task<OperatorTripSettlement?> FindByOperatorTripAsync(Guid operatorId, Guid tripId, CancellationToken cancellationToken)
        => _db.OperatorTripSettlements.FirstOrDefaultAsync(x => x.OperatorId == operatorId && x.TripId == tripId, cancellationToken);
    public async Task<OperatorTripSettlement?> GetForUpdateAsync(Guid settlementId, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM vietride_payment.operator_trip_settlements WHERE id = {settlementId} FOR UPDATE",
            cancellationToken);
        return await _db.OperatorTripSettlements.FirstOrDefaultAsync(
            settlement => settlement.Id == settlementId,
            cancellationToken);
    }
}
