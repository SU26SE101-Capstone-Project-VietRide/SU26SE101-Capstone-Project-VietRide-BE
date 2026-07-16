using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class OperatorWalletTransactionRepository : IOperatorWalletTransactionRepository
{
    private readonly PaymentDbContext _db;
    public OperatorWalletTransactionRepository(PaymentDbContext db) => _db = db;
    public Task<OperatorWalletTransaction?> GetByIdAsync(Guid id, CancellationToken ct) => _db.OperatorWalletTransactions.FirstOrDefaultAsync(x => x.Id == id, ct);
    public async Task<OperatorWalletTransaction> AddAsync(OperatorWalletTransaction entity, CancellationToken ct) { await _db.OperatorWalletTransactions.AddAsync(entity, ct); return entity; }
    public void Update(OperatorWalletTransaction entity) => throw new NotSupportedException("Operator wallet transactions are immutable.");
    public void Remove(OperatorWalletTransaction entity) => throw new NotSupportedException("Operator wallet transactions are immutable.");
    public IQueryable<OperatorWalletTransaction> Query() => _db.OperatorWalletTransactions;
    public IQueryable<OperatorWalletTransaction> QueryNoTracking() => _db.OperatorWalletTransactions.AsNoTracking();
}
