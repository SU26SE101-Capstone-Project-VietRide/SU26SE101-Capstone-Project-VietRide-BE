using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class PlatformWalletRepository : IPlatformWalletRepository
{
    private readonly PaymentDbContext _db;
    private readonly IClock _clock;

    public PlatformWalletRepository(PaymentDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
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

    public Task<PlatformWalletTransaction?> FindTransactionByReferenceAsync(
        PlatformWalletTransactionRef referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => _db.PlatformWalletTransactions.FirstOrDefaultAsync(
            transaction => transaction.ReferenceType == referenceType
                && transaction.ReferenceId == referenceId,
            cancellationToken);

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
            [],
            cancellationToken);
    }

    public Task<PlatformWalletTransaction> CreditWithLinksAsync(
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        IReadOnlyCollection<PlatformWalletTransactionLinkInput> links,
        CancellationToken cancellationToken)
        => ApplyAsync(
            PlatformWalletTransactionType.CREDIT,
            amount,
            referenceType,
            referenceId,
            note,
            links,
            cancellationToken);

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
            [],
            cancellationToken);
    }

    public Task<PlatformWalletTransaction> DebitWithLinksAsync(
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        IReadOnlyCollection<PlatformWalletTransactionLinkInput> links,
        CancellationToken cancellationToken)
        => ApplyAsync(
            PlatformWalletTransactionType.DEBIT,
            amount,
            referenceType,
            referenceId,
            note,
            links,
            cancellationToken);

    private async Task<PlatformWalletTransaction> ApplyAsync(
        PlatformWalletTransactionType type,
        Money amount,
        PlatformWalletTransactionRef referenceType,
        Guid? referenceId,
        string? note,
        IReadOnlyCollection<PlatformWalletTransactionLinkInput> links,
        CancellationToken cancellationToken)
    {
        if (amount.Amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Platform wallet transaction amount must be positive.");
        ArgumentNullException.ThrowIfNull(links);
        if (links.Count > 0 && links.Sum(item => checked(item.AllocatedAmount)) != amount.Amount)
            throw new ArgumentException("Platform wallet link allocations must equal the movement amount.", nameof(links));
        if (links.GroupBy(item => (item.LinkType, item.ReferenceId)).Any(group => group.Count() > 1))
            throw new ArgumentException("Platform wallet link allocations must be unique by type and reference.", nameof(links));

        await _db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(hashtext('payment:platform-wallet')::bigint)",
            cancellationToken);

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
                    .SetProperty(candidate => candidate.UpdatedAt, _clock.UtcNow),
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
            note,
            _clock.UtcNow);

        await _db.PlatformWalletTransactions.AddAsync(transaction, cancellationToken);
        if (links.Count > 0)
        {
            var entities = links.Select(item => PlatformWalletTransactionLink.Create(
                transaction.Id,
                item.LinkType,
                item.AllocatedAmount,
                item.OperatorId,
                item.TripId,
                item.ReferenceId,
                item.ReferenceCode));
            await _db.PlatformWalletTransactionLinks.AddRangeAsync(entities, cancellationToken);
        }

        return transaction;
    }
}
