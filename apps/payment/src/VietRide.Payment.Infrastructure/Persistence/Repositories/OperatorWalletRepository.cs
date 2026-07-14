using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class OperatorWalletRepository : IOperatorWalletRepository
{
    private readonly PaymentDbContext _db;
    public OperatorWalletRepository(PaymentDbContext db) => _db = db;
    public Task<OperatorWallet?> GetByIdAsync(Guid id, CancellationToken ct) => FindByOperatorIdAsync(id, ct);
    public async Task<OperatorWallet> AddAsync(OperatorWallet entity, CancellationToken ct) { await _db.OperatorWallets.AddAsync(entity, ct); return entity; }
    public void Update(OperatorWallet entity) => _db.OperatorWallets.Update(entity);
    public void Remove(OperatorWallet entity) => _db.OperatorWallets.Remove(entity);
    public IQueryable<OperatorWallet> Query() => _db.OperatorWallets;
    public IQueryable<OperatorWallet> QueryNoTracking() => _db.OperatorWallets.AsNoTracking();
    public Task<OperatorWallet?> FindByOperatorIdAsync(Guid operatorId, CancellationToken cancellationToken)
        => _db.OperatorWallets.FirstOrDefaultAsync(x => x.OperatorId == operatorId, cancellationToken);
}
