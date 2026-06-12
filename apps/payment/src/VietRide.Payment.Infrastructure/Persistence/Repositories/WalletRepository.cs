using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class WalletRepository : IWalletRepository
{
    private readonly PaymentDbContext _db;

    public WalletRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<Wallet?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Wallets.FirstOrDefaultAsync(wallet => wallet.UserId == id, ct);

    public async Task<Wallet> AddAsync(Wallet entity, CancellationToken ct)
    {
        await _db.Wallets.AddAsync(entity, ct);
        return entity;
    }

    public void Update(Wallet entity)
        => _db.Wallets.Update(entity);

    public void Remove(Wallet entity)
        => _db.Wallets.Remove(entity);

    public IQueryable<Wallet> Query()
        => _db.Wallets;

    public IQueryable<Wallet> QueryNoTracking()
        => _db.Wallets.AsNoTracking();

    public async Task<bool> EnsureBootstrapWalletAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_payment.wallets (user_id, balance, currency, row_version)
            VALUES ({userId}, 0, 'VND', 0)
            ON CONFLICT (user_id) DO NOTHING
            """, cancellationToken);

        return rows == 1;
    }
}
