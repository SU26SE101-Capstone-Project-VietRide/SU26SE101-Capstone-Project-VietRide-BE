using Microsoft.EntityFrameworkCore;
using VietRide.Payment.Application.Abstractions.Repositories;

namespace VietRide.Payment.Infrastructure.Persistence.Repositories;

internal sealed class FinancialActorPrivacyStore : IFinancialActorPrivacyStore
{
    public const string DeletedDisplayName = "Người dùng đã xóa";

    private readonly PaymentDbContext _db;

    public FinancialActorPrivacyStore(PaymentDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsDeletedWithLockAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Financial actor privacy check requires an active transaction.");

        await AcquireActorLockAsync(userId, cancellationToken);
        return await _db.DeletedFinancialActorMarkers
            .AsNoTracking()
            .AnyAsync(item => item.UserId == userId, cancellationToken);
    }

    public async Task<int> MarkDeletedAndRedactAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        if (_db.Database.CurrentTransaction is not null)
            return await MarkDeletedAndRedactCoreAsync(userId, cancellationToken);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var affectedRows = await MarkDeletedAndRedactCoreAsync(userId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return affectedRows;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private async Task<int> MarkDeletedAndRedactCoreAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await AcquireActorLockAsync(userId, cancellationToken);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO vietride_payment.deleted_financial_actor_markers (user_id, deleted_at)
            VALUES ({userId}, CURRENT_TIMESTAMP)
            ON CONFLICT (user_id) DO NOTHING;
            """, cancellationToken);

        var settlements = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE vietride_payment.operator_trip_settlements
            SET settled_by_display_name = {DeletedDisplayName},
                settled_by_email = NULL,
                settled_by_role = NULL,
                settled_by_snapshot_resolved = TRUE,
                row_version = row_version + 1,
                updated_at = CURRENT_TIMESTAMP
            WHERE settled_by_user_id = {userId}
              AND (settled_by_display_name IS DISTINCT FROM {DeletedDisplayName}
                   OR settled_by_email IS NOT NULL
                   OR settled_by_role IS NOT NULL
                   OR settled_by_snapshot_resolved = FALSE);
            """, cancellationToken);
        var transactions = await _db.PlatformWalletTransactions
            .Where(item => item.ActorUserId == userId
                && (item.ActorDisplayName != DeletedDisplayName
                    || item.ActorEmail != null
                    || item.ActorRole != null
                    || !item.ActorSnapshotResolved))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.ActorDisplayName, DeletedDisplayName)
                .SetProperty(item => item.ActorEmail, (string?)null)
                .SetProperty(item => item.ActorRole, (string?)null)
                .SetProperty(item => item.ActorSnapshotResolved, true), cancellationToken);

        return settlements + transactions;
    }

    private Task<int> AcquireActorLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        var lockKey = $"payment:financial-actor:{userId:N}";
        return _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext({lockKey})::bigint)",
            cancellationToken);
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("Financial actor user id is required.", nameof(userId));
    }
}
