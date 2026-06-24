using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class PaymentRepository : IPaymentRepository
{
    private readonly PaymentDbContext _db;

    public PaymentRepository(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _db.Payments.FirstOrDefaultAsync(payment => payment.Id == id, ct);

    public async Task<PaymentEntity> AddAsync(PaymentEntity entity, CancellationToken ct)
    {
        await _db.Payments.AddAsync(entity, ct);
        return entity;
    }

    public void Update(PaymentEntity entity)
        => _db.Payments.Update(entity);

    public void Remove(PaymentEntity entity)
        => _db.Payments.Remove(entity);

    public IQueryable<PaymentEntity> Query()
        => _db.Payments;

    public IQueryable<PaymentEntity> QueryNoTracking()
        => _db.Payments.AsNoTracking();

    public async Task<PaymentEntity?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        => await _db.Payments.FirstOrDefaultAsync(
            payment => payment.IdempotencyKey == idempotencyKey,
            cancellationToken);

    public async Task<PaymentEntity?> FindByReferenceAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => await _db.Payments.FirstOrDefaultAsync(
            payment => payment.ReferenceType == referenceType && payment.ReferenceId == referenceId,
            cancellationToken);

    public async Task AcquirePaymentReferenceLockAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        var lockKey = $"payment:{referenceType}:{referenceId:N}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey})::bigint)",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<WalletTransaction> DebitWalletBookingPaymentAsync(
        Guid userId,
        Guid bookingId,
        Money amount,
        CancellationToken cancellationToken)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new PaymentInsufficientWalletException("Wallet was not found for the requested user.");

        if (wallet.Balance < amount)
        {
            throw new PaymentInsufficientWalletException("Wallet balance is insufficient for the booking payment.");
        }

        var (before, after) = wallet.Debit(amount);
        var transaction = WalletTransaction.CreateBookingPaymentDebit(userId, bookingId, amount, before, after);

        await _db.WalletTransactions.AddAsync(transaction, cancellationToken);
        return transaction;
    }

    public async Task<bool> TryMarkRefundedByReferenceAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        DateTimeOffset refundedAt,
        CancellationToken cancellationToken)
    {
        // Guarded on status = SUCCEEDED so a re-delivered wallet.credited event is an idempotent
        // no-op (0 rows). updated_at is maintained by the trg_payments_updated_at DB trigger.
        var affected = await _db.Payments
            .Where(payment => payment.ReferenceType == referenceType
                && payment.ReferenceId == referenceId
                && payment.Status == PaymentStatus.SUCCEEDED)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(payment => payment.Status, PaymentStatus.REFUNDED)
                    .SetProperty(payment => payment.RefundedAt, refundedAt),
                cancellationToken)
            .ConfigureAwait(false);

        return affected > 0;
    }

    public async Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectOlderThanAsync(
        DateTimeOffset expiresBefore,
        DateTimeOffset expiredAt,
        CancellationToken cancellationToken)
        => await _db.Payments
            .FromSqlInterpolated($"""
                UPDATE vietride_payment.payments
                SET status = 'EXPIRED',
                    expired_at = {expiredAt},
                    updated_at = {expiredAt}
                WHERE status = 'PENDING_REDIRECT'
                  AND method = 'VNPAY'
                  AND reference_type = 'BOOKING'
                  AND created_at < {expiresBefore}
                RETURNING *
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
}
