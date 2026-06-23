using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class PlatformWalletRepository : IPlatformWalletRepository
{
    private readonly PaymentDbContext _db;

    public PlatformWalletRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<PlatformWallet?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.PlatformWallets.FirstOrDefaultAsync(wallet => wallet.Id == id, ct);

    public async Task<PlatformWallet> AddAsync(PlatformWallet entity, CancellationToken ct)
    {
        await _db.PlatformWallets.AddAsync(entity, ct);
        return entity;
    }

    public void Update(PlatformWallet entity)
        => _db.PlatformWallets.Update(entity);

    public void Remove(PlatformWallet entity)
        => _db.PlatformWallets.Remove(entity);

    public IQueryable<PlatformWallet> Query()
        => _db.PlatformWallets;

    public IQueryable<PlatformWallet> QueryNoTracking()
        => _db.PlatformWallets.AsNoTracking();

    public async Task<PlatformWallet> GetSingletonAsync(CancellationToken cancellationToken)
    {
        return await _db.PlatformWallets.AsNoTracking().SingleAsync(cancellationToken);
    }

    public Task<PlatformWalletTransaction> CreditAsync(
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        CancellationToken cancellationToken)
    {
        return ApplyAsync(
            PlatformWalletTransactionType.CREDIT,
            amount,
            referenceType,
            referenceId,
            note,
            cancellationToken);
    }

    public Task<PlatformWalletTransaction> DebitAsync(
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        CancellationToken cancellationToken)
    {
        return ApplyAsync(
            PlatformWalletTransactionType.DEBIT,
            amount,
            referenceType,
            referenceId,
            note,
            cancellationToken);
    }

    private async Task<PlatformWalletTransaction> ApplyAsync(
        PlatformWalletTransactionType type,
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        CancellationToken cancellationToken)
    {
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Platform wallet transaction amount must be positive.");

        var wallet = await GetSingletonAsync(cancellationToken);
        var balanceBefore = wallet.Balance;
        var balanceAfterAmount = type == PlatformWalletTransactionType.CREDIT
            ? balanceBefore.Amount + amount.Amount
            : balanceBefore.Amount - amount.Amount;

        if (balanceAfterAmount < 0)
            throw new InvalidOperationException("Platform wallet balance cannot be negative.");

        var balanceAfter = Money.FromRaw(balanceAfterAmount);
        var updatedRows = await _db.PlatformWallets
            .Where(candidate => candidate.Id == wallet.Id && candidate.RowVersion == wallet.RowVersion)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.Balance, balanceAfter)
                    .SetProperty(candidate => candidate.RowVersion, candidate => candidate.RowVersion + 1)
                    .SetProperty(candidate => candidate.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken);

        if (updatedRows != 1)
            throw new DbUpdateConcurrencyException("Platform wallet was updated by another transaction.");

        var transaction = PlatformWalletTransaction.Create(
            type,
            amount,
            balanceBefore,
            balanceAfter,
            referenceType,
            referenceId,
            note);

        await _db.PlatformWalletTransactions.AddAsync(transaction, cancellationToken);

        return transaction;
    }
}
