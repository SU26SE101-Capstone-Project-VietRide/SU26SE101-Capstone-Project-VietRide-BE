using System.Text.Json;
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

    public async Task<PaymentEntity?> FindLatestByReferenceAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => await _db.Payments
            .Where(payment => payment.ReferenceType == referenceType && payment.ReferenceId == referenceId)
            .OrderByDescending(payment => payment.CreatedAt)
            .ThenByDescending(payment => payment.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PaymentEntity?> FindSucceededByReferenceAsync(
        PaymentReferenceType referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
        => await _db.Payments
            .Where(payment => payment.ReferenceType == referenceType
                && payment.ReferenceId == referenceId
                && payment.Status == PaymentStatus.SUCCEEDED)
            .OrderBy(payment => payment.CreatedAt)
            .ThenBy(payment => payment.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<PaymentEntity>> ListLatestSubscriptionPaymentsAsync(
        IReadOnlyCollection<Guid> upgradeAttemptIds,
        CancellationToken cancellationToken)
        => await _db.Payments
            .AsNoTracking()
            .Where(payment => payment.ReferenceType == PaymentReferenceType.SUBSCRIPTION
                && upgradeAttemptIds.Contains(payment.ReferenceId))
            .GroupBy(payment => payment.ReferenceId)
            .Select(group => group
                .OrderByDescending(payment => payment.CreatedAt)
                .ThenByDescending(payment => payment.Id)
                .First())
            .ToListAsync(cancellationToken);

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

    public async Task<PaymentEntity?> FindVnPayPaymentByTxnRefAsync(
        string vnPayTxnRef,
        CancellationToken cancellationToken)
        => await _db.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(
                payment => payment.VnPayTxnRef == vnPayTxnRef
                    && payment.Method == PaymentMethod.VNPAY,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<PaymentEntity?> LockAndReloadAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var trackedPayment = _db.Payments.Local.FirstOrDefault(payment => payment.Id == paymentId);
        if (trackedPayment is not null)
        {
            _db.Entry(trackedPayment).State = EntityState.Detached;
        }

        return await _db.Payments
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_payment.payments
                WHERE id = {paymentId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken)
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

    public async Task<WalletTransaction> DebitWalletPaymentAsync(
        Guid userId,
        Guid referenceId,
        Money amount,
        WalletTransactionRef walletRef,
        CancellationToken cancellationToken)
    {
        var wallet = await _db.Wallets.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? throw new PaymentInsufficientWalletException("Wallet was not found for the requested user.");

        if (wallet.Balance < amount)
        {
            throw new PaymentInsufficientWalletException("Wallet balance is insufficient for the payment.");
        }

        var (before, after) = wallet.Debit(amount);
        var transaction = WalletTransaction.CreatePaymentDebit(userId, referenceId, amount, before, after, walletRef);

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

    public async Task<PaymentEntity?> FindSucceededBookingPaymentByAllocationAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
        => (await ListSucceededBookingFundingPaymentsByAllocationAsync(
            bookingId,
            cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public async Task<IReadOnlyList<PaymentEntity>> ListSucceededBookingFundingPaymentsByAllocationAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var containment = JsonSerializer.Serialize(
            new
            {
                allocations = new[]
                {
                    new
                    {
                        referenceType = "BOOKING",
                        referenceId = bookingId,
                    },
                },
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return await _db.Payments
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_payment.payments
                WHERE status IN (
                          'SUCCEEDED'::vietride_payment.payment_status,
                          'REFUNDED'::vietride_payment.payment_status
                      )
                  AND method IN (
                          'WALLET'::vietride_payment.payment_method,
                          'VNPAY'::vietride_payment.payment_method
                      )
                  AND succeeded_at IS NOT NULL
                  AND context @> CAST({containment} AS jsonb)
                  AND (
                      (
                          reference_type = 'BOOKING'::vietride_payment.payment_reference_type
                          AND reference_id = {bookingId}
                      )
                      OR (
                          reference_type = 'BOOKING_GROUP'::vietride_payment.payment_reference_type
                      )
                  )
                ORDER BY succeeded_at, created_at, id
                LIMIT 2
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PaymentEntity>> ListBookingPaymentAttemptsByAllocationAsync(
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        var containment = JsonSerializer.Serialize(
            new
            {
                allocations = new[]
                {
                    new
                    {
                        referenceType = "BOOKING",
                        referenceId = bookingId,
                    },
                },
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return await _db.Payments
            .FromSqlInterpolated($"""
                SELECT *
                FROM vietride_payment.payments
                WHERE method IN (
                          'WALLET'::vietride_payment.payment_method,
                          'VNPAY'::vietride_payment.payment_method
                      )
                  AND context @> CAST({containment} AS jsonb)
                  AND (
                      (
                          reference_type = 'BOOKING'::vietride_payment.payment_reference_type
                          AND reference_id = {bookingId}
                      )
                      OR reference_type = 'BOOKING_GROUP'::vietride_payment.payment_reference_type
                  )
                ORDER BY created_at, id
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> TryMarkRefundedByIdAsync(
        Guid paymentId,
        DateTimeOffset refundedAt,
        CancellationToken cancellationToken)
    {
        var affected = await _db.Payments
            .Where(payment => payment.Id == paymentId && payment.Status == PaymentStatus.SUCCEEDED)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(payment => payment.Status, PaymentStatus.REFUNDED)
                    .SetProperty(payment => payment.RefundedAt, refundedAt),
                cancellationToken)
            .ConfigureAwait(false);

        return affected == 1;
    }

    public async Task AcquireRefundReconciliationLockAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var lockKey = $"payment-refund-reconciliation:{paymentId:N}";
        await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext({lockKey})::bigint)",
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectDueAsync(
        DateTimeOffset legacyCreatedAtOrBefore,
        DateTimeOffset expiredAt,
        CancellationToken cancellationToken)
        => await _db.Payments
            .FromSqlInterpolated($"""
                UPDATE vietride_payment.payments
                SET status = 'EXPIRED'::vietride_payment.payment_status,
                    expired_at = {expiredAt},
                    updated_at = {expiredAt}
                WHERE status = 'PENDING_REDIRECT'::vietride_payment.payment_status
                  AND method = 'VNPAY'::vietride_payment.payment_method
                  AND reference_type IN (
                      'BOOKING'::vietride_payment.payment_reference_type,
                      'BOOKING_GROUP'::vietride_payment.payment_reference_type,
                      'SUBSCRIPTION'::vietride_payment.payment_reference_type,
                      'PARCEL'::vietride_payment.payment_reference_type,
                      'PARCEL_ADDITIONAL'::vietride_payment.payment_reference_type)
                  AND (
                      due_at <= {expiredAt}
                      OR (due_at IS NULL AND created_at <= {legacyCreatedAtOrBefore}))
                RETURNING *
                """)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
}
