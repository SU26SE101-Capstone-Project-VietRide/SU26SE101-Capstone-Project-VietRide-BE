using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class OperatorLedgerEntryRepository : IOperatorLedgerEntryRepository
{
    private readonly PaymentDbContext _db;
    public OperatorLedgerEntryRepository(PaymentDbContext db) => _db = db;
    public Task<OperatorLedgerEntry?> GetByIdAsync(Guid id, CancellationToken ct) => _db.OperatorLedgerEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<OperatorLedgerEntry> AddAsync(OperatorLedgerEntry entity, CancellationToken ct) { await _db.OperatorLedgerEntries.AddAsync(entity, ct); return entity; }
    public void Update(OperatorLedgerEntry entity) => throw new NotSupportedException("Operator ledger is immutable.");
    public void Remove(OperatorLedgerEntry entity) => throw new NotSupportedException("Operator ledger is immutable.");
    public IQueryable<OperatorLedgerEntry> Query() => _db.OperatorLedgerEntries;
    public IQueryable<OperatorLedgerEntry> QueryNoTracking() => _db.OperatorLedgerEntries.AsNoTracking();
    public Task<long> SumTripNetAmountAsync(Guid operatorId, Guid tripId, CancellationToken cancellationToken)
        => _db.OperatorLedgerEntries.Where(x => x.OperatorId == operatorId && x.TripId == tripId).SumAsync(x => x.Amount, cancellationToken);
}
