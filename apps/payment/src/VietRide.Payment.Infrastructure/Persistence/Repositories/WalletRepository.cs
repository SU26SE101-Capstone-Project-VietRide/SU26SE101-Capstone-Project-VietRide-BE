using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

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

    public async Task<WalletTransaction> CreditTopUpAsync(
        Guid userId,
        Money amount,
        Guid topUpRequestId,
        CancellationToken cancellationToken)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new InvalidOperationException($"Wallet for user {userId} was not found.");

        var balanceBefore = wallet.Balance;
        wallet.Credit(amount);
        var balanceAfter = wallet.Balance;

        var transaction = WalletTransaction.Create(
            userId,
            WalletTransactionType.CREDIT,
            amount,
            balanceBefore,
            balanceAfter,
            WalletTransactionRef.TOP_UP,
            topUpRequestId,
            "VNPay wallet top-up");

        await _db.WalletTransactions.AddAsync(transaction, cancellationToken);
        return transaction;
    }
}
